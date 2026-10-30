// Copyright (c) RazorConsole. All rights reserved.

using System.CommandLine;
using System.CommandLine.Parsing;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RazorConsole.Cli.Operations;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Input;

namespace RazorConsole.Cli.Commands;

internal sealed partial class CliCommandLine
{
    private const int MaxTerminalSize = 1000;

    /// <summary>
    /// Builds every verb and returns <c>start</c>, the one verb that takes the arguments after <c>--</c>.
    /// </summary>
    private Command AddVerbs()
    {
        var start = AddSessionVerbs();
        AddInputVerbs();
        AddScreenVerbs();
        AddWaitVerbs();
        AddAppVerbs();
        AddToolVerbs();
        return start;
    }

    private Command AddSessionVerbs()
    {
        var startName = SessionNameArgument("The session name. Defaults to s1, s2, …");
        var exe = new Option<string?>("--exe") { Description = "Start an executable.", HelpName = "path" };
        var dll = new Option<string?>("--dll") { Description = "Start an assembly with dotnet.", HelpName = "path" };
        var project = new Option<string?>("--project") { Description = "Build and start a project.", HelpName = "dir" };
        var width = IntOption("--width", StartRequest.DefaultWidth, 1, MaxTerminalSize, "The terminal width in columns.");
        var height = IntOption("--height", StartRequest.DefaultHeight, 1, MaxTerminalSize, "The terminal height in rows.");
        var environment = new Option<List<KeyValuePair<string, string>>>("--env")
        {
            Description = "Set an environment variable for the app. Repeat for more.",
            HelpName = "NAME=value",
            Arity = ArgumentArity.ZeroOrMore,
            CustomParser = ParseEnvironment,
        };
        var cwd = new Option<string?>("--cwd") { Description = "The app's working directory.", HelpName = "dir" };
        var term = new Option<string>("--term")
        {
            Description = "The TERM value on Linux and macOS.",
            DefaultValueFactory = _ => StartRequest.DefaultTerminalType,
        };
        var noWait = new Option<bool>("--no-wait") { Description = "Return without waiting for the first frame." };
        var start = new Command("start", "Start an app in a new PTY session. Pass the app's arguments after --.")
        {
            startName, exe, dll, project, width, height, environment, cwd, term, noWait,
        };
        Add(_root, start, context =>
        {
            var targets = new (string Name, string? Path, StartTargetKind Kind)[]
            {
                ("--exe", context.Get(exe), StartTargetKind.Exe),
                ("--dll", context.Get(dll), StartTargetKind.Dll),
                ("--project", context.Get(project), StartTargetKind.Project),
            }.Where(static target => target.Path is not null).ToList();
            if (targets.Count != 1)
            {
                throw new CliUsageException("Use exactly one of --exe, --dll or --project.");
            }

            var variables = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (name, value) in context.Get(environment) ?? [])
            {
                variables[name] = value;
            }

            return new StartRequest(
                context.Get(startName),
                targets[0].Kind,
                FullPath(targets[0].Path!),
                context.AppArguments,
                context.Get(width),
                context.Get(height),
                variables,
                context.Get(cwd) is { } directory ? FullPath(directory) : null,
                context.Get(term)!,
                context.Get(noWait));
        });

        var stopName = SessionNameArgument("The session to stop. Defaults to the current session.");
        Add(_root, new Command("stop", "Stop a session: Ctrl+C first, then kill after --timeout (default 5s).") { stopName }, context =>
            new StopRequest(context.Get(stopName), context.Get(_timeout) ?? StopRequest.DefaultTimeout));

        var restartName = SessionNameArgument("The session to restart. Defaults to the current session.");
        Add(_root, new Command("restart", "Stop a session and start it again with the same options.") { restartName }, context =>
            new RestartRequest(context.Get(restartName)));

        var columns = IntArgument("cols", 1, MaxTerminalSize, "The new width in columns.");
        var rows = IntArgument("rows", 1, MaxTerminalSize, "The new height in rows.");
        Add(_root, new Command("resize", "Resize the session's terminal.") { columns, rows }, context =>
            new ResizeRequest(context.Get(columns), context.Get(rows)));

        Add(_root, new Command("list", "List the sessions."), static _ => new ListRequest());

        var useName = new Argument<string>("name") { Description = "The session to make current." };
        useName.Validators.Add(static result => ValidateSessionName(result, result.GetValueOrDefault<string>()));
        Add(_root, new Command("use", "Make a session the current one.") { useName }, context =>
            new UseRequest(context.Get(useName)!));

        var processId = IntArgument("pid", 1, int.MaxValue, "The process ID of a running app with diagnostics enabled.");
        var attachName = SessionNameArgument("The session name. Defaults to a1, a2, …");
        Add(_root, new Command("attach", "Attach to a running app through its diagnostics endpoint (app layer only).") { processId, attachName }, context =>
            new AttachRequest(context.Get(processId), context.Get(attachName)));

        var detachName = SessionNameArgument("The attached session to drop. Defaults to the current session.");
        Add(_root, new Command("detach", "Drop an attached session. The app keeps running.") { detachName }, context =>
            new DetachRequest(context.Get(detachName)));

        return start;
    }

    private void AddInputVerbs()
    {
        var keys = new Argument<List<ConsoleKeyInfo>>("key")
        {
            Description = "Keys such as Tab, Enter, Ctrl+C, Shift+Tab, F5 or a.",
            Arity = ArgumentArity.OneOrMore,
            CustomParser = ParseKeys,
        };
        var keysElement = ElementOption("Focus this element first (through the app, like focus --set), then press the keys.");
        Add(_root, new Command("keys", "Press keys. They are real input: the app handles them, so Tab may do more than move focus.") { keys, keysElement }, context =>
            new KeysRequest(context.Get(keys)!, context.Get(keysElement)));

        var typeText = new Argument<string>("text") { Description = "The text to type." };
        Add(_root, new Command("type", "Type text, one key per character.") { typeText }, context =>
            new TypeRequest(context.Get(typeText)!));

        var pasteText = new Argument<string>("text") { Description = "The text to paste." };
        Add(_root, new Command("paste", "Paste text, bracketed when the app enabled bracketed paste.") { pasteText }, context =>
            new PasteRequest(context.Get(pasteText)!));

        var rawData = new Argument<string>("escaped")
        {
            Description = @"Terminal input with escapes: \e, \xHH, \uHHHH, \r, \n, \t, \0, \\.",
            CustomParser = ParseEscapes,
        };
        Add(_root, new Command("raw", "Write raw terminal input.") { rawData }, context =>
            new RawRequest(context.Get(rawData)!));

        var clickX = CoordinateArgument("x", ArgumentArity.ZeroOrOne);
        var clickY = CoordinateArgument("y", ArgumentArity.ZeroOrOne);
        var clickText = new Option<string?>("--text") { Description = "Click this text on the screen." };
        var nth = IntOption("--nth", 1, 1, int.MaxValue, "Which occurrence of --text to click.");
        var clickElement = ElementOption("Click this element.");
        var clickButton = ButtonOption();
        var doubleClick = new Option<bool>("--double") { Description = "Click twice." };
        var click = new Command("click", "Click a cell (<x> <y>), text on the screen (--text) or an element (--element).")
        {
            clickX, clickY, clickText, nth, clickElement, clickButton, doubleClick,
        };
        Add(_root, click, context =>
        {
            var x = context.Get(clickX);
            var y = context.Get(clickY);
            if (x.HasValue != y.HasValue)
            {
                throw new CliUsageException("Give both <x> and <y>.");
            }

            var text = context.Get(clickText);
            var element = context.Get(clickElement);
            var targets = (x.HasValue ? 1 : 0) + (text is null ? 0 : 1) + (element is null ? 0 : 1);
            if (targets != 1)
            {
                throw new CliUsageException("Click exactly one target: <x> <y>, --text or --element.");
            }

            if (context.IsGiven(nth) && text is null)
            {
                throw new CliUsageException("--nth needs --text.");
            }

            return new ClickRequest(x, y, text, context.Get(nth), element, context.Get(clickButton) ?? 0, context.Get(doubleClick));
        });

        var mouseKind = new Argument<string>("action") { Description = "What the mouse does." };
        mouseKind.AcceptOnlyFromAmong("move", "down", "up", "wheel");
        var mouseX = CoordinateArgument("x", ArgumentArity.ExactlyOne);
        var mouseY = CoordinateArgument("y", ArgumentArity.ExactlyOne);
        var mouseButton = ButtonOption();
        var delta = new Option<int>("--delta")
        {
            Description = "For wheel, the rows to scroll: positive down, negative up.",
            DefaultValueFactory = _ => 3,
            CustomParser = static result =>
            {
                var value = ParseNumber(result, "--delta", int.MinValue, int.MaxValue);
                if (value == 0)
                {
                    result.AddError("--delta must not be 0.");
                }

                return (int?)value ?? 3;
            },
        };
        Add(_root, new Command("mouse", "Send one mouse event.") { mouseKind, mouseX, mouseY, mouseButton, delta }, context =>
        {
            var kind = context.Get(mouseKind) switch
            {
                "move" => TerminalMouseKind.Move,
                "down" => TerminalMouseKind.Down,
                "up" => TerminalMouseKind.Up,
                _ => TerminalMouseKind.Wheel,
            };
            if (context.IsGiven(delta) && kind != TerminalMouseKind.Wheel)
            {
                throw new CliUsageException("--delta only applies to wheel.");
            }

            return new MouseRequest(kind, context.Get(mouseX)!.Value, context.Get(mouseY)!.Value, context.Get(mouseButton) ?? 0, context.Get(delta));
        });
    }

    private void AddScreenVerbs()
    {
        var format = new Option<string>("--format")
        {
            Description = "text, ansi (with colors) or json (with cells).",
            DefaultValueFactory = _ => "text",
        };
        format.AcceptOnlyFromAmong("text", "ansi", "json");
        var region = new Option<DiagnosticsRegion?>("--region")
        {
            Description = "Only this part of the screen.",
            HelpName = "x,y,w,h",
            CustomParser = ParseRegion,
        };
        Add(_root, new Command("screen", "Show the screen.") { format, region }, context =>
            new ScreenRequest(
                context.Get(format) switch
                {
                    "ansi" => ScreenFormat.Ansi,
                    "json" => ScreenFormat.Json,
                    _ => ScreenFormat.Text,
                },
                context.Get(region)));

        var last = IntOption("--last", 1, 1, FramesRequest.MaxFrames, "How many recent frames to show.");
        Add(_root, new Command("frames", "Show recent frames of a PTY session.") { last }, context =>
            new FramesRequest(context.Get(last)));

        var from = OptionalLongOption("--from", "The older frame. Defaults to the one before --to.", "frame");
        var to = OptionalLongOption("--to", "The newer frame. Defaults to the latest.", "frame");
        Add(_root, new Command("diff", "Show the lines that changed between two frames.") { from, to }, context =>
            new DiffRequest(context.Get(from), context.Get(to)));

        var since = OptionalLongOption("--since", "Start at this offset from an earlier call's next.", "offset");
        var maxBytes = OptionalIntOption("--max-bytes", 1, "Return at most this much output.");
        Add(_root, new Command("output", "Show the raw output of a PTY session.") { since, maxBytes }, context =>
            new OutputRequest(context.Get(since), context.Get(maxBytes)));
    }

    private void AddWaitVerbs()
    {
        var wait = new WaitOptions();
        var waitCommand = new Command("wait", "Wait until a condition holds, up to --timeout.");
        wait.AddTo(waitCommand);
        Add(_root, waitCommand, context => new WaitRequest(wait.Build(context)));

        var assert = new WaitOptions();
        var not = new Option<bool>("--not") { Description = "Pass when the condition does not hold." };
        var assertCommand = new Command("assert", "Check a condition now; exit 1 when it fails.") { not };
        assert.AddTo(assertCommand);
        Add(_root, assertCommand, context => new AssertRequest(assert.Build(context), context.Get(not)));
    }

    private void AddAppVerbs()
    {
        var treeSelector = new Argument<string?>("selector") { Description = "Show only this subtree.", Arity = ArgumentArity.ZeroOrOne };
        var depth = OptionalIntOption("--depth", 0, "How many levels to show.");
        var noText = new Option<bool>("--no-text") { Description = "Leave out text content." };
        var noLayout = new Option<bool>("--no-layout") { Description = "Leave out layout." };
        Add(_root, new Command("tree", "Show the component tree.") { treeSelector, depth, noText, noLayout }, context =>
            new TreeRequest(context.Get(treeSelector), context.Get(depth), !context.Get(noText), !context.Get(noLayout)));

        var findSelector = new Argument<string>("selector") { Description = "What to find." };
        var first = new Option<bool>("--first") { Description = "Return only the first match." };
        var all = new Option<bool>("--all") { Description = "Return every match (the default)." };
        Add(_root, new Command("find", "Find elements.") { findSelector, first, all }, context =>
        {
            RequireAtMostOne(("--first", context.Get(first)), ("--all", context.Get(all)));
            return new FindRequest(context.Get(findSelector)!, context.Get(first));
        });

        var inspectSelector = new Argument<string>("selector") { Description = "The element to inspect." };
        Add(_root, new Command("inspect", "Show one element in detail.") { inspectSelector }, context =>
            new InspectRequest(context.Get(inspectSelector)!));

        var inspectX = CoordinateArgument("x", ArgumentArity.ExactlyOne);
        var inspectY = CoordinateArgument("y", ArgumentArity.ExactlyOne);
        Add(_root, new Command("inspect-at", "Show the element and the cell at a screen position.") { inspectX, inspectY }, context =>
            new InspectAtRequest(context.Get(inspectX)!.Value, context.Get(inspectY)!.Value));

        var list = new Option<bool>("--list") { Description = "List the focusable elements (the default)." };
        var set = new Option<string?>("--set") { Description = "Focus the element with this key.", HelpName = "key" };
        var next = new Option<bool>("--next") { Description = "Move focus forward." };
        var previous = new Option<bool>("--prev") { Description = "Move focus back." };
        Add(_root, new Command("focus", "Show or move focus. --set, --next and --prev change the app's focus state directly and fire focus events only; to press the key, use 'keys Tab'.") { list, set, next, previous }, context =>
        {
            var key = context.Get(set);
            RequireAtMostOne(("--list", context.Get(list)), ("--set", key is not null), ("--next", context.Get(next)), ("--prev", context.Get(previous)));
            var action = key is not null ? FocusAction.Set
                : context.Get(next) ? FocusAction.Next
                : context.Get(previous) ? FocusAction.Previous
                : FocusAction.List;
            return new FocusRequest(action, key);
        });

        var go = new Option<string?>("--go") { Description = "Navigate to this route.", HelpName = "uri" };
        Add(_root, new Command("route", "Show the current route, or navigate.") { go }, context =>
            new RouteRequest(context.Get(go)));

        var errorsSince = OptionalLongOption("--since", "Only entries after this sequence number.", "seq");
        var errorsFollow = new Option<bool>("--follow") { Description = "Keep streaming new entries as NDJSON until Ctrl+C or --timeout." };
        Add(_root, new Command("errors", "Show errors the app caught.") { errorsSince, errorsFollow }, context =>
            new ErrorsRequest(context.Get(errorsSince), context.Get(errorsFollow)));

        var logsSince = OptionalLongOption("--since", "Only entries after this sequence number.", "seq");
        var level = new Option<string?>("--level") { Description = "The minimum level: Trace, Debug, Information, Warning, Error or Critical." };
        level.Validators.Add(static result =>
        {
            if (result.GetValueOrDefault<string?>() is { } value && !TryParseLogLevel(value, out _))
            {
                result.AddError($"'{value}' is not a log level. Use Trace, Debug, Information, Warning, Error or Critical.");
            }
        });
        var category = new Option<string?>("--category") { Description = "Only categories that start with this." };
        var logsFollow = new Option<bool>("--follow") { Description = "Keep streaming new entries as NDJSON until Ctrl+C or --timeout." };
        Add(_root, new Command("logs", "Show the app's log entries.") { logsSince, level, category, logsFollow }, context =>
        {
            var name = context.Get(level) is { } value && TryParseLogLevel(value, out var parsed) ? parsed.ToString() : null;
            return new LogsRequest(context.Get(logsSince), name, context.Get(category), context.Get(logsFollow));
        });

        Add(_root, new Command("stats", "Show render statistics."), static _ => new StatsRequest());
    }

    private void AddToolVerbs()
    {
        var fix = new Option<bool>("--fix") { Description = "Remove stale state files that the checks find." };
        Add(_root, new Command("doctor", "Check the environment, the session host and a real terminal session. Starts the session host if none runs, and stops it again afterwards.") { fix }, context =>
            new DoctorRequest(context.Get(fix)));

        var host = new Command("host", "Manage the background session host.");
        _root.Add(host);
        Add(host, new Command("status", "Show whether the host runs, its version and its sessions."), static _ => new HostStatusRequest());
        Add(host, new Command("stop", "Stop the host and every session in it."), static _ => new HostStopRequest());

        var idleTimeout = OptionalDurationOption("--idle-timeout", "Exit after this long with no sessions and no clients. Defaults to 10m.");
        var hostRun = new Command("__host", "Run the session host in this process.") { idleTimeout };
        hostRun.Hidden = true;
        Add(_root, hostRun, context => new HostRunRequest(context.Get(idleTimeout) ?? HostRunRequest.DefaultIdleTimeout));
    }

    private static Option<string?> ElementOption(string description)
        => new("--element") { Description = description, HelpName = "selector" };

    private static Argument<int?> CoordinateArgument(string name, ArgumentArity arity) => new(name)
    {
        Description = $"The zero-based {(name == "x" ? "column" : "row")}.",
        Arity = arity,
        CustomParser = result => (int?)ParseNumber(result, $"<{name}>", 0, int.MaxValue),
    };

    private static Option<int?> ButtonOption() => new("--button")
    {
        Description = "left (the default), middle or right.",
        HelpName = "button",
        CustomParser = static result =>
        {
            if (!CliValueParsers.TryParseButton(result.Tokens[^1].Value, out var button, out var error))
            {
                result.AddError(error);
            }

            return button;
        },
    };

    private static bool TryParseLogLevel(string text, out LogLevel level)
        => Enum.TryParse(text, ignoreCase: true, out level)
            && Enum.IsDefined(level)
            && level != LogLevel.None
            && char.IsAsciiLetter(text[0]);

    private static List<KeyValuePair<string, string>> ParseEnvironment(ArgumentResult result)
    {
        var variables = new List<KeyValuePair<string, string>>();
        foreach (var token in result.Tokens)
        {
            if (!CliValueParsers.TryParseEnvironmentVariable(token.Value, out var variable, out var error))
            {
                result.AddError(error);
                return variables;
            }

            variables.Add(variable);
        }

        return variables;
    }

    private static List<ConsoleKeyInfo> ParseKeys(ArgumentResult result)
    {
        var keys = new List<ConsoleKeyInfo>();
        foreach (var token in result.Tokens)
        {
            if (!CliValueParsers.TryParseKey(token.Value, out var key, out var error))
            {
                result.AddError(error);
                return keys;
            }

            keys.Add(key);
        }

        return keys;
    }

    private static string? ParseEscapes(ArgumentResult result)
    {
        if (!CliValueParsers.TryDecodeEscapes(result.Tokens[^1].Value, out var value, out var error))
        {
            result.AddError(error);
        }

        return value;
    }

    private static DiagnosticsRegion? ParseRegion(ArgumentResult result)
    {
        if (!CliValueParsers.TryParseRegion(result.Tokens[^1].Value, out var region, out var error))
        {
            result.AddError(error);
        }

        return region;
    }

    /// <summary>
    /// The condition options of <c>wait</c> and <c>assert</c>. Each command gets its own instances, because an
    /// option belongs to one parent.
    /// </summary>
    private sealed class WaitOptions
    {
        private readonly Option<string?> _text = new("--text") { Description = "Text appears on the screen." };
        private readonly Option<string?> _regex = new("--regex") { Description = "A line on the screen matches this .NET regex.", HelpName = "pattern" };
        private readonly Option<string?> _gone = new("--gone") { Description = "Text is no longer on the screen.", HelpName = "text" };
        private readonly Option<TimeSpan?> _idle = OptionalDurationOption("--idle", "The output stays quiet this long.");
        private readonly Option<bool> _exit = new("--exit") { Description = "The app exits." };
        private readonly Option<string?> _element = ElementOption("An element matches this selector.");
        private readonly Option<string?> _focus = new("--focus") { Description = "The element with this key has focus.", HelpName = "key" };
        private readonly Option<string?> _route = new("--route") { Description = "The current route is this URI.", HelpName = "uri" };

        public WaitOptions()
        {
            _regex.Validators.Add(static result =>
            {
                if (result.GetValueOrDefault<string?>() is not { } pattern)
                {
                    return;
                }

                try
                {
                    _ = new Regex(pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
                }
                catch (ArgumentException exception)
                {
                    result.AddError($"--regex: {exception.Message}");
                }
            });
        }

        public void AddTo(Command command)
        {
            command.Add(_text);
            command.Add(_regex);
            command.Add(_gone);
            command.Add(_idle);
            command.Add(_exit);
            command.Add(_element);
            command.Add(_focus);
            command.Add(_route);
        }

        public WaitCondition Build(VerbContext context)
        {
            var text = context.Get(_text);
            var regex = context.Get(_regex);
            var gone = context.Get(_gone);
            var idle = context.Get(_idle);
            var exit = context.Get(_exit);
            var element = context.Get(_element);
            var focus = context.Get(_focus);
            var route = context.Get(_route);
            var conditions = new List<WaitCondition>();
            if (text is not null)
            {
                conditions.Add(new WaitCondition(WaitConditionKind.Text, text));
            }

            if (regex is not null)
            {
                conditions.Add(new WaitCondition(WaitConditionKind.Regex, regex));
            }

            if (gone is not null)
            {
                conditions.Add(new WaitCondition(WaitConditionKind.Gone, gone));
            }

            if (idle is not null)
            {
                conditions.Add(new WaitCondition(WaitConditionKind.Idle, Idle: idle));
            }

            if (exit)
            {
                conditions.Add(new WaitCondition(WaitConditionKind.Exit));
            }

            if (element is not null)
            {
                conditions.Add(new WaitCondition(WaitConditionKind.Element, element));
            }

            if (focus is not null)
            {
                conditions.Add(new WaitCondition(WaitConditionKind.Focus, focus));
            }

            if (route is not null)
            {
                conditions.Add(new WaitCondition(WaitConditionKind.Route, route));
            }

            return conditions.Count == 1
                ? conditions[0]
                : throw new CliUsageException("Give exactly one condition: --text, --regex, --gone, --idle, --exit, --element, --focus or --route.");
        }
    }
}
