// Copyright (c) RazorConsole. All rights reserved.

using System.Text.Json;
using ModelContextProtocol.Protocol;
using RazorConsole.Cli.Commands;
using RazorConsole.Cli.Operations;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Input;

namespace RazorConsole.Cli.Mcp;

/// <summary>
/// How a tool changes state, which decides its annotations.
/// </summary>
internal enum McpToolKind
{
    /// <summary>Only reads: <c>readOnlyHint</c>.</summary>
    ReadOnly,

    /// <summary>Changes state without destroying any.</summary>
    Changes,

    /// <summary>Sends input, navigates, stops or restarts: <c>destructiveHint</c>.</summary>
    Destructive,
}

/// <summary>
/// What the result of a tool needs after the operation ran.
/// </summary>
internal enum McpShape
{
    Plain,
    SessionInfo,
    Assert,
    Screenshot,
    Frames,
    Diff,
    Output,
    Tree,
    Find,
    Logs,
    Errors,
}

/// <summary>
/// What a tool call asks for once its arguments are checked.
/// </summary>
/// <param name="Request">The CLI request to run.</param>
/// <param name="Options">The global options: the session and the timeout.</param>
/// <param name="Limit">For get_logs and get_errors, how many entries to return.</param>
/// <param name="MaxBytes">For get_output, how many bytes the host may return.</param>
/// <param name="TreeBudget">For get_tree, how many characters the tree may have: small unless the caller asked for a depth.</param>
internal sealed record McpBound(CliRequest Request, CliGlobalOptions Options, int Limit = 0, int MaxBytes = 0, int TreeBudget = 0);

/// <summary>
/// One tool: its listing and how its arguments become a <see cref="CliRequest"/>.
/// </summary>
internal sealed class McpTool
{
    public required string Name { get; init; }

    public required string Title { get; init; }

    public required string Description { get; init; }

    public required McpSchema Schema { get; init; }

    public required McpToolKind Kind { get; init; }

    public bool Idempotent { get; init; }

    public McpShape Shape { get; init; }

    /// <summary>
    /// Whether the tool works on a session, so it takes <c>session</c>.
    /// </summary>
    public bool Scoped { get; init; }

    public required Func<McpArguments, CliRequest?> Bind { get; init; }

    /// <summary>
    /// The longest timeout the tool accepts.
    /// </summary>
    public TimeSpan MaxTimeout { get; init; } = McpTools.MaxTimeout;

    public Tool ToProtocol() => new()
    {
        Name = Name,
        Title = Title,
        Description = Description,
        InputSchema = Schema.Build(),
        Annotations = new ToolAnnotations
        {
            Title = Title,
            ReadOnlyHint = Kind == McpToolKind.ReadOnly,
            DestructiveHint = Kind == McpToolKind.Destructive,
            IdempotentHint = Kind == McpToolKind.ReadOnly || Idempotent,
            OpenWorldHint = false,
        },
    };

    /// <summary>
    /// Checks the arguments and builds what to run. Returns the problems when there are any.
    /// </summary>
    public McpBound? TryBind(IDictionary<string, JsonElement>? values, CliGlobalOptions defaults, out IReadOnlyList<string> errors)
    {
        var arguments = new McpArguments(values);
        var session = Scoped ? arguments.String("session") : null;
        if (session is not null && !CliValueParsers.TryValidateSessionName(session, out var sessionError))
        {
            arguments.AddError($"session: {sessionError}");
        }

        var timeout = arguments.Duration("timeout", MaxTimeout);

        var limit = 0;
        var maxBytes = 0;
        var treeBudget = 0;
        CliRequest? request = null;
        try
        {
            request = Bind(arguments);
            if (Shape is McpShape.Logs or McpShape.Errors)
            {
                limit = arguments.Integer("limit", 1, McpResultLimiter.MaxLogLimit) ?? McpResultLimiter.DefaultLogLimit;
            }

            if (Shape == McpShape.Tree)
            {
                treeBudget = arguments.Has("depth") ? McpResultLimiter.MaxResultCharacters : McpResultLimiter.DefaultTreeBudget;
            }

            if (Shape == McpShape.Output)
            {
                maxBytes = (request as OutputRequest)?.MaxBytes ?? McpResultLimiter.DefaultOutputBytes;
            }
        }
        catch (CliUsageException exception)
        {
            arguments.AddError(exception.Message);
        }

        arguments.RejectUnknown();
        errors = arguments.Errors.Distinct().ToList();
        if (arguments.Errors.Count > 0 || request is null)
        {
            return null;
        }

        var options = new CliGlobalOptions(true, session ?? defaults.Session, timeout ?? defaults.Timeout);
        return new McpBound(request, options, limit, maxBytes, treeBudget);
    }
}

/// <summary>
/// The tools of the MCP server. Each one maps onto the CLI request of the same verb, so what it returns is what
/// <c>razorconsole &lt;verb&gt; --json</c> prints.
/// </summary>
internal static class McpTools
{
    public static readonly TimeSpan MaxTimeout = TimeSpan.FromMinutes(10);
    public static readonly TimeSpan MaxWaitTimeout = TimeSpan.FromMinutes(5);

    private const string SessionAdvice = " Always pass `session` (the name start_session returned).";

    private const string Selectors = "Selectors are `<kind>:<value>`: `hook:<name>` (a data-vnode-hook attribute, the most stable), `focus:<key>`, `key:<key>`, `id:<id>`, `text:<substring>`, `text~:<regex>`, `tag:<name>[attr=value]`, `path:/0/2/1`.";

    private const string ConditionAdvice = "Give exactly one condition: `text` (on the screen), `regex` (a .NET regex matched per line), `gone` (text no longer on the screen), `idle` (output quiet for this long: seconds as a number, or a string such as 500ms), `exit` (the app exits), `selector` (an element matches this selector), `focus` (this focus key has focus) or `route` (the current route is this URI).";

    public static IReadOnlyList<McpTool> All { get; } = Create();

    public static McpTool? Find(string name) => All.FirstOrDefault(tool => tool.Name == name);

    internal static string Describe(TimeSpan value)
        => value.TotalMinutes >= 1 ? $"{value.TotalMinutes:0}m" : $"{value.TotalSeconds:0}s";

    private static List<McpTool> Create() =>
    [
        // --- Sessions -----------------------------------------------------------------------------------------------
        Define(
            "start_session",
            "Start an app in a PTY session",
            "Starts a RazorConsole app in a pseudoterminal and waits for its first frame. Give exactly one of `exe`, `dll` or `project`. Returns the session name: pass it as `session` to every other tool. Fails with `process_exited` (with the output tail) when the app dies at startup, `pty_unsupported` on an OS without pseudoterminals. Relative paths resolve against the MCP server's working directory.",
            McpToolKind.Changes,
            schema => schema
                .String("name", "A name for the session. Defaults to s1, s2, ...")
                .String("exe", "Path of an executable to start.")
                .String("dll", "Path of an assembly to start with dotnet.")
                .String("project", "Directory of a project to build and start (slow: it builds first).")
                .StringArray("args", "Arguments for the app.")
                .String("framework", "With `project`: the target framework to run, such as net9.0.")
                .String("configuration", "With `project`: the build configuration, such as Release. Defaults to Debug.")
                .Integer("width", "Terminal width in columns. Default 120.", minimum: 1, maximum: 1000)
                .Integer("height", "Terminal height in rows. Default 40.", minimum: 1, maximum: 1000)
                .StringMap("env", "Environment variables for the app, as NAME: value.")
                .String("cwd", "The app's working directory.")
                .String("term", "The TERM value on Linux and macOS. Default xterm-256color.")
                .Boolean("noWait", "Return without waiting for the first frame."),
            BindStart,
            scoped: false),
        Define(
            "list_sessions",
            "List sessions",
            "Lists the sessions of the session host, including ones the developer started in a terminal. Empty when no host runs.",
            McpToolKind.ReadOnly,
            static schema => { },
            static _ => new ListRequest(),
            scoped: false),
        Define(
            "session_info",
            "Show one session",
            "Shows one session: kind (pty or attached), pid, size, whether it exited and its exit code, and whether the app layer (tree, focus, logs) is connected. Fails with `session_not_found`.",
            McpToolKind.ReadOnly,
            static schema => { },
            static _ => new ListRequest(),
            shape: McpShape.SessionInfo,
            requireSession: true),
        Define(
            "stop_session",
            "Stop a session",
            "Stops a session: Ctrl+C first, then kill after `timeout` (default 5s). Returns the exit code. Attached sessions are not stopped; use detach_session.",
            McpToolKind.Destructive,
            static schema => { },
            static arguments => new StopRequest(null, arguments.Duration("timeout") ?? StopRequest.DefaultTimeout)),
        Define(
            "restart_session",
            "Restart a session",
            "Stops a session and starts it again with the same options. Use it to get a fresh app state. Needs a PTY session (`terminal_layer_unavailable` for an attached one).",
            McpToolKind.Destructive,
            static schema => { },
            static _ => new RestartRequest(null)),
        Define(
            "resize_session",
            "Resize the terminal",
            "Resizes the session's terminal. Use it to check how the layout reacts to small or large terminals, then wait_for `idle`.",
            McpToolKind.Changes,
            static schema => schema
                .Integer("cols", "New width in columns.", required: true, minimum: 1, maximum: 1000)
                .Integer("rows", "New height in rows.", required: true, minimum: 1, maximum: 1000),
            static arguments => new ResizeRequest(
                arguments.Integer("cols", 1, 1000, required: true) ?? 0,
                arguments.Integer("rows", 1, 1000, required: true) ?? 0),
            idempotent: true),
        Define(
            "attach_app",
            "Attach to a running app",
            "Attaches to a RazorConsole app that already runs with diagnostics enabled (Development or Debug) through its diagnostics endpoint. The session has the app layer only: no raw output, frames or resize. Returns the session name to pass as `session`.",
            McpToolKind.Changes,
            static schema => schema
                .Integer("pid", "The process id of the app.", required: true, minimum: 1)
                .String("name", "A name for the session. Defaults to a1, a2, ..."),
            static arguments => new AttachRequest(arguments.Integer("pid", 1, required: true) ?? 0, arguments.String("name")),
            scoped: false),
        Define(
            "detach_session",
            "Drop an attached session",
            "Drops an attached session. The app keeps running.",
            McpToolKind.Changes,
            static schema => { },
            static _ => new DetachRequest(null)),

        // --- Input --------------------------------------------------------------------------------------------------
        Define(
            "send_keys",
            "Press keys",
            "Presses keys as real input; the app handles them (Tab may do more than move focus). Key names: `Tab`, `Enter`, `Escape`, `Shift+Tab`, `Ctrl+C`, `F5`, `Up`, a single character. With `selector`, focuses that element first. Then wait_for the result instead of sleeping. " + Selectors,
            McpToolKind.Destructive,
            static schema => schema
                .StringArray("keys", "The keys, in order.", required: true)
                .String("selector", "A selector to focus first."),
            static arguments => new KeysRequest(ParseKeys(arguments), arguments.String("selector"))),
        Define(
            "type_text",
            "Type text",
            "Types text, one key per character, into whatever has focus.",
            McpToolKind.Destructive,
            static schema => schema.String("text", "The text to type.", required: true),
            static arguments => new TypeRequest(arguments.String("text", required: true) ?? string.Empty)),
        Define(
            "paste_text",
            "Paste text",
            "Pastes text, bracketed when the app enabled bracketed paste.",
            McpToolKind.Destructive,
            static schema => schema.String("text", "The text to paste.", required: true),
            static arguments => new PasteRequest(arguments.String("text", required: true) ?? string.Empty)),
        Define(
            "send_raw",
            "Send raw terminal input",
            "Writes raw terminal input, for escape sequences the other input tools cannot make. Escapes: `\\e`, `\\xHH`, `\\uHHHH`, `\\r`, `\\n`, `\\t`, `\\0`, `\\\\`.",
            McpToolKind.Destructive,
            static schema => schema.String("data", "The input, with escapes.", required: true),
            static arguments => new RawRequest(Decode(arguments))),
        Define(
            "click",
            "Click",
            "Clicks with real mouse input. Give exactly one target: `x` and `y` (zero-based cell), `text` (text on the screen; `nth` picks the occurrence, from 1) or `selector` (an element). Fails with `element_not_found`, `ambiguous_selector` (lists candidates), `element_not_visible`. " + Selectors,
            McpToolKind.Destructive,
            static schema => schema
                .Integer("x", "Zero-based column, with `y`.", minimum: 0)
                .Integer("y", "Zero-based row, with `x`.", minimum: 0)
                .String("text", "Click this text on the screen.")
                .Integer("nth", "With `text`: which occurrence, from 1. Default 1.", minimum: 1)
                .String("selector", "Click the element this selector matches.")
                .String("button", "left (default), middle or right.", false, "left", "middle", "right")
                .Boolean("double", "Click twice."),
            BindClick),
        Define(
            "mouse",
            "Send one mouse event",
            "Sends one mouse event at a cell: `move`, `down`, `up` or `wheel` (`delta` rows, positive down). For a simple click use click.",
            McpToolKind.Destructive,
            static schema => schema
                .String("action", "What the mouse does.", true, "move", "down", "up", "wheel")
                .Integer("x", "Zero-based column.", required: true, minimum: 0)
                .Integer("y", "Zero-based row.", required: true, minimum: 0)
                .String("button", "left (default), middle or right.", false, "left", "middle", "right")
                .Integer("delta", "For wheel: rows to scroll, positive down, negative up, not 0. Default 3."),
            BindMouse),

        // --- Screen -------------------------------------------------------------------------------------------------
        Define(
            "screenshot",
            "Read the screen",
            "Reads the screen as text (what a user sees), or with `format` json as styled cells with the cursor, active buffer and mouse and paste modes. `ansi` keeps the colors as escapes. `region` limits it to part of the screen. Long results are cut at the bottom with a `truncated` marker; ask for a `region` then.",
            McpToolKind.ReadOnly,
            static schema => schema
                .String("format", "text (default), json or ansi.", false, "text", "json", "ansi")
                .String("region", "Only this part: `x,y,width,height`."),
            BindScreen,
            shape: McpShape.Screenshot),
        Define(
            "get_frames",
            "Read recent frames",
            "Returns the last frames of a PTY session (up to 50), newest last, to see what changed over time. Long results drop the oldest frames and say so in `truncated`. Large `last` values can exceed the result size cap: prefer a small `last` or diff_frames.",
            McpToolKind.ReadOnly,
            static schema => schema.Integer("last", "How many frames. Default 1.", minimum: 1, maximum: FramesRequest.MaxFrames),
            static arguments => new FramesRequest(arguments.Integer("last", 1, FramesRequest.MaxFrames) ?? 1),
            shape: McpShape.Frames),
        Define(
            "diff_frames",
            "Diff two frames",
            "Shows the lines that changed between two frames (numbers from get_frames). Defaults to the latest frame and the one before it.",
            McpToolKind.ReadOnly,
            static schema => schema
                .Integer("from", "The older frame number.", minimum: 0)
                .Integer("to", "The newer frame number.", minimum: 0),
            static arguments => new DiffRequest(arguments.Long("from"), arguments.Long("to")),
            shape: McpShape.Diff),
        Define(
            "get_output",
            "Read raw output",
            "Reads the raw terminal output of a PTY session, with escape sequences. Offsets count UTF-8 bytes. Returns at most `maxBytes` (default 64 KiB); when more may follow, `truncated` says so: call again with `since` set to `next`.",
            McpToolKind.ReadOnly,
            static schema => schema
                .Integer("since", "Start at this offset: the `next` of an earlier call.", minimum: 0)
                .Integer("maxBytes", "At most this many bytes. Default 65536, at most 1048576.", minimum: 1, maximum: McpResultLimiter.MaxOutputBytes),
            static arguments => new OutputRequest(arguments.Long("since"), arguments.Integer("maxBytes", 1, McpResultLimiter.MaxOutputBytes) ?? McpResultLimiter.DefaultOutputBytes),
            shape: McpShape.Output),

        // --- Wait and assert ----------------------------------------------------------------------------------------
        Define(
            "wait_for",
            "Wait for a condition",
            "Waits until a condition holds, up to `timeout` (default 10s, at most 5m). Use it after input instead of sleeping. Fails with `timeout` (the screen is in `error.data.screen`) or `process_exited`. " + ConditionAdvice,
            McpToolKind.ReadOnly,
            static schema => ConditionSchema(schema),
            static arguments => new WaitRequest(ParseCondition(arguments)),
            maxTimeout: MaxWaitTimeout),
        Define(
            "assert",
            "Check a condition now",
            "Checks a condition right now, without waiting. A condition that does not hold is not an error: the result has `passed: false` and `failure` with the screen (and the tree for selector conditions). `not` inverts the check. " + ConditionAdvice,
            McpToolKind.ReadOnly,
            static schema => ConditionSchema(schema).Boolean("not", "Pass when the condition does not hold."),
            static arguments => new AssertRequest(ParseCondition(arguments), arguments.Boolean("not")),
            shape: McpShape.Assert,
            maxTimeout: MaxWaitTimeout),

        // --- App layer ----------------------------------------------------------------------------------------------
        Define(
            "get_tree",
            "Read the component tree",
            "Reads the app's component tree with layout rectangles, focus keys and events. Needs the app layer (a Development or Debug app). Start with a small `depth` or a `selector`; use find_elements to locate things. Without `depth` it shows 3 levels and about 20k characters; cut nodes show `childCount` and `truncated` says how to get more. " + Selectors,
            McpToolKind.ReadOnly,
            static schema => schema
                .String("selector", "Show only this subtree.")
                .Integer("depth", "How many levels to show. Default 3; an explicit depth may return up to 100k characters.", minimum: 0)
                .Boolean("includeText", "Include text nodes. Default true.")
                .Boolean("includeLayout", "Include layout rectangles. Default true."),
            static arguments => new TreeRequest(
                arguments.String("selector"),
                arguments.Integer("depth", 0) ?? McpResultLimiter.DefaultTreeDepth,
                arguments.Boolean("includeText", true),
                arguments.Boolean("includeLayout", true)),
            shape: McpShape.Tree),
        Define(
            "find_elements",
            "Find elements",
            "Finds elements by selector. No match is not an error: `matches` is empty. At most 100 matches are returned. " + Selectors,
            McpToolKind.ReadOnly,
            static schema => schema
                .String("selector", "What to find.", required: true)
                .Boolean("first", "Return only the first match."),
            static arguments => new FindRequest(arguments.Text("selector", required: true) ?? string.Empty, arguments.Boolean("first")),
            shape: McpShape.Find),
        Define(
            "inspect_element",
            "Inspect one element",
            "Shows one element in detail: attributes, events, layout, focus. Fails with `element_not_found` or `ambiguous_selector`. " + Selectors,
            McpToolKind.ReadOnly,
            static schema => schema.String("selector", "The element.", required: true),
            static arguments => new InspectRequest(arguments.Text("selector", required: true) ?? string.Empty)),
        Define(
            "inspect_at",
            "See what is at a cell",
            "Shows the element and the screen cell at a position. Use it to find out what a visible thing is.",
            McpToolKind.ReadOnly,
            static schema => schema
                .Integer("x", "Zero-based column.", required: true, minimum: 0)
                .Integer("y", "Zero-based row.", required: true, minimum: 0),
            static arguments => new InspectAtRequest(
                arguments.Integer("x", 0, required: true) ?? 0,
                arguments.Integer("y", 0, required: true) ?? 0)),
        Define(
            "get_focus",
            "List focus targets",
            "Lists the focusable elements in tab order and which one has focus. Use it to check keyboard accessibility.",
            McpToolKind.ReadOnly,
            static schema => { },
            static _ => new FocusRequest(FocusAction.List, null)),
        Define(
            "set_focus",
            "Move focus",
            "Changes the app's focus state directly (focus events fire, no key is pressed): `key` focuses that focus key, `move` goes to the next or previous target. To press the key as a user would, use send_keys.",
            McpToolKind.Changes,
            static schema => schema
                .String("key", "The focus key to focus.")
                .String("move", "next or prev.", false, "next", "prev"),
            BindFocus),
        Define(
            "get_route",
            "Read the current route",
            "Returns the current route (URI and base-relative path) of the app.",
            McpToolKind.ReadOnly,
            static schema => { },
            static _ => new RouteRequest(null)),
        Define(
            "navigate",
            "Navigate to a route",
            "Navigates the app to a route, the way a link would.",
            McpToolKind.Destructive,
            static schema => schema.String("uri", "The route, such as /settings.", required: true),
            static arguments => new RouteRequest(arguments.Text("uri", required: true) ?? string.Empty)),
        Define(
            "get_errors",
            "Read caught errors",
            "Returns the errors the app's renderer caught (a throwing handler or component), oldest first, at most `limit` (default 100). The cursor: pass `next` as `since` to get the following ones. A crash that ended the process is in `process_exited` instead.",
            McpToolKind.ReadOnly,
            static schema => schema
                .Integer("since", "Start at this sequence number: the `next` of an earlier call.", minimum: 0)
                .Integer("limit", "At most this many entries. Default 100, at most 1000.", minimum: 1, maximum: McpResultLimiter.MaxLogLimit),
            static arguments => new ErrorsRequest(arguments.Long("since"), false),
            shape: McpShape.Errors),
        Define(
            "get_logs",
            "Read the app's logs",
            "Returns the app's log entries, oldest first, at most `limit` (default 100). The cursor: pass `next` as `since` to page. Long messages are cut and marked.",
            McpToolKind.ReadOnly,
            static schema => schema
                .Integer("since", "Start at this sequence number: the `next` of an earlier call.", minimum: 0)
                .String("level", "The minimum level.", false, "Trace", "Debug", "Information", "Warning", "Error", "Critical")
                .String("category", "Only categories that start with this.")
                .Integer("limit", "At most this many entries. Default 100, at most 1000.", minimum: 1, maximum: McpResultLimiter.MaxLogLimit),
            BindLogs,
            shape: McpShape.Logs),
        Define(
            "get_stats",
            "Read render statistics",
            "Returns the rendering pipeline, how many frames were delivered, the latest frame and the uptime.",
            McpToolKind.ReadOnly,
            static schema => { },
            static _ => new StatsRequest()),

        // --- Environment --------------------------------------------------------------------------------------------
        Define(
            "doctor",
            "Check the setup",
            "Checks the environment, the session host and a real terminal session. Run it when another tool fails with `host_unavailable`, `pty_unsupported` or `host_version_mismatch`. Fails with `checks_failed`; the checks are in the result either way. With `fix`, removes stale state files.",
            McpToolKind.Changes,
            static schema => schema.Boolean("fix", "Remove stale state files that the checks find."),
            static arguments => new DoctorRequest(arguments.Boolean("fix")),
            idempotent: true,
            scoped: false),
    ];

    private static McpTool Define(
        string name,
        string title,
        string description,
        McpToolKind kind,
        Action<McpSchema> schema,
        Func<McpArguments, CliRequest?> bind,
        McpShape shape = McpShape.Plain,
        bool scoped = true,
        bool idempotent = false,
        bool requireSession = false,
        TimeSpan? maxTimeout = null)
    {
        var builder = new McpSchema();
        if (scoped)
        {
            builder.String("session", "The session name returned by start_session or attach_app. Always pass it: the current session is shared with the developer.", requireSession);
        }

        schema(builder);
        builder.Duration("timeout", $"How long this call may take. Default 10s, at most {Describe(maxTimeout ?? MaxTimeout)}.");

        return new McpTool
        {
            Name = name,
            Title = title,
            Description = scoped ? description + SessionAdvice : description,
            Schema = builder,
            Kind = kind,
            Idempotent = idempotent,
            Shape = shape,
            Scoped = scoped,
            Bind = bind,
            MaxTimeout = maxTimeout ?? MaxTimeout,
        };
    }

    private static McpSchema ConditionSchema(McpSchema schema) => schema
        .String("text", "Text is on the screen.")
        .String("regex", "A line on the screen matches this .NET regex.")
        .String("gone", "Text is no longer on the screen.")
        .Duration("idle", "The output stays quiet this long.")
        .Boolean("exit", "The app exits.")
        .String("selector", "An element matches this selector.")
        .String("focus", "The element with this focus key has focus.")
        .String("route", "The current route is this URI.");

    private static WaitCondition ParseCondition(McpArguments arguments)
    {
        var conditions = new List<WaitCondition>();
        if (arguments.Text("text") is { } text)
        {
            conditions.Add(new WaitCondition(WaitConditionKind.Text, text));
        }

        if (arguments.Text("regex") is { } regex)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(regex, System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));
                conditions.Add(new WaitCondition(WaitConditionKind.Regex, regex));
            }
            catch (ArgumentException exception)
            {
                arguments.AddError($"regex: {exception.Message}");
            }
        }

        if (arguments.Text("gone") is { } gone)
        {
            conditions.Add(new WaitCondition(WaitConditionKind.Gone, gone));
        }

        if (arguments.Duration("idle") is { } idle)
        {
            conditions.Add(new WaitCondition(WaitConditionKind.Idle, Idle: idle));
        }

        if (arguments.Boolean("exit"))
        {
            conditions.Add(new WaitCondition(WaitConditionKind.Exit));
        }

        if (arguments.Text("selector") is { } selector)
        {
            conditions.Add(new WaitCondition(WaitConditionKind.Element, selector));
        }

        if (arguments.Text("focus") is { } focus)
        {
            conditions.Add(new WaitCondition(WaitConditionKind.Focus, focus));
        }

        if (arguments.Text("route") is { } route)
        {
            conditions.Add(new WaitCondition(WaitConditionKind.Route, route));
        }

        if (conditions.Count != 1)
        {
            if (arguments.Errors.Count == 0)
            {
                arguments.AddError("Give exactly one condition: text, regex, gone, idle, exit, selector, focus or route.");
            }

            return new WaitCondition(WaitConditionKind.Exit);
        }

        return conditions[0];
    }

    private static CliRequest BindStart(McpArguments arguments)
    {
        var targets = new (string Name, string? Path, StartTargetKind Kind)[]
        {
            ("exe", arguments.Text("exe"), StartTargetKind.Exe),
            ("dll", arguments.Text("dll"), StartTargetKind.Dll),
            ("project", arguments.Text("project"), StartTargetKind.Project),
        }.Where(static target => target.Path is not null).ToList();
        if (targets.Count != 1)
        {
            throw new CliUsageException("Use exactly one of exe, dll or project.");
        }

        var framework = arguments.Text("framework");
        var configuration = arguments.Text("configuration");
        if ((framework is not null || configuration is not null) && targets[0].Kind != StartTargetKind.Project)
        {
            throw new CliUsageException("framework and configuration can only be used with project.");
        }

        var name = arguments.String("name");
        if (name is not null && !CliValueParsers.TryValidateSessionName(name, out var nameError))
        {
            throw new CliUsageException($"name: {nameError}");
        }

        var directory = Environment.CurrentDirectory;
        return new StartRequest(
            name,
            targets[0].Kind,
            Path.GetFullPath(targets[0].Path!, directory),
            arguments.StringArray("args") ?? [],
            arguments.Integer("width", 1, 1000) ?? StartRequest.DefaultWidth,
            arguments.Integer("height", 1, 1000) ?? StartRequest.DefaultHeight,
            arguments.StringMap("env"),
            arguments.Text("cwd") is { } cwd ? Path.GetFullPath(cwd, directory) : null,
            arguments.Text("term") ?? StartRequest.DefaultTerminalType,
            arguments.Boolean("noWait"),
            framework,
            configuration);
    }

    private static List<ConsoleKeyInfo> ParseKeys(McpArguments arguments)
    {
        var keys = new List<ConsoleKeyInfo>();
        foreach (var text in arguments.StringArray("keys", required: true) ?? [])
        {
            if (CliValueParsers.TryParseKey(text, out var key, out var error))
            {
                keys.Add(key);
            }
            else
            {
                arguments.AddError($"keys: {error}");
            }
        }

        return keys;
    }

    private static string Decode(McpArguments arguments)
    {
        var text = arguments.String("data", required: true);
        if (text is null)
        {
            return string.Empty;
        }

        if (!CliValueParsers.TryDecodeEscapes(text, out var value, out var error))
        {
            arguments.AddError($"data: {error}");
            return string.Empty;
        }

        return value;
    }

    private static int Button(McpArguments arguments)
    {
        if (arguments.String("button") is not { } text)
        {
            return 0;
        }

        if (!CliValueParsers.TryParseButton(text, out var button, out var error))
        {
            arguments.AddError($"button: {error}");
            return 0;
        }

        return button;
    }

    private static CliRequest BindClick(McpArguments arguments)
    {
        var x = arguments.Integer("x", 0);
        var y = arguments.Integer("y", 0);
        if (x.HasValue != y.HasValue)
        {
            throw new CliUsageException("Give both x and y.");
        }

        var text = arguments.Text("text");
        var element = arguments.Text("selector");
        var targets = (x.HasValue ? 1 : 0) + (text is null ? 0 : 1) + (element is null ? 0 : 1);
        if (targets != 1)
        {
            throw new CliUsageException("Click exactly one target: x and y, text or selector.");
        }

        var nth = arguments.Integer("nth", 1);
        if (nth.HasValue && text is null)
        {
            throw new CliUsageException("nth needs text.");
        }

        return new ClickRequest(x, y, text, nth ?? 1, element, Button(arguments), arguments.Boolean("double"));
    }

    private static CliRequest BindMouse(McpArguments arguments)
    {
        var kind = arguments.String("action", required: true) switch
        {
            "move" => TerminalMouseKind.Move,
            "down" => TerminalMouseKind.Down,
            "up" => TerminalMouseKind.Up,
            "wheel" => TerminalMouseKind.Wheel,
            null => TerminalMouseKind.Move,
            var other => throw new CliUsageException($"action: '{other}' is not move, down, up or wheel."),
        };
        var delta = arguments.Integer("delta");
        if (delta.HasValue && kind != TerminalMouseKind.Wheel)
        {
            throw new CliUsageException("delta only applies to wheel.");
        }

        if (delta == 0)
        {
            throw new CliUsageException("delta must not be 0.");
        }

        return new MouseRequest(
            kind,
            arguments.Integer("x", 0, required: true) ?? 0,
            arguments.Integer("y", 0, required: true) ?? 0,
            Button(arguments),
            delta ?? 3);
    }

    private static CliRequest BindScreen(McpArguments arguments)
    {
        var format = arguments.String("format") switch
        {
            null or "text" => ScreenFormat.Text,
            "json" => ScreenFormat.Json,
            "ansi" => ScreenFormat.Ansi,
            var other => throw new CliUsageException($"format: '{other}' is not text, json or ansi."),
        };
        DiagnosticsRegion? region = null;
        if (arguments.String("region") is { } text)
        {
            if (CliValueParsers.TryParseRegion(text, out var parsed, out var error))
            {
                region = parsed;
            }
            else
            {
                arguments.AddError($"region: {error}");
            }
        }

        return new ScreenRequest(format, region);
    }

    private static CliRequest BindFocus(McpArguments arguments)
    {
        var key = arguments.Text("key");
        var move = arguments.String("move");
        if ((key is null) == (move is null))
        {
            throw new CliUsageException("Give exactly one of key or move.");
        }

        return move switch
        {
            null => new FocusRequest(FocusAction.Set, key),
            "next" => new FocusRequest(FocusAction.Next, null),
            "prev" => new FocusRequest(FocusAction.Previous, null),
            var other => throw new CliUsageException($"move: '{other}' is not next or prev."),
        };
    }

    private static CliRequest BindLogs(McpArguments arguments)
    {
        string? level = null;
        if (arguments.String("level") is { } text)
        {
            if (CliCommandLine.TryParseLogLevel(text, out var parsed))
            {
                level = parsed.ToString();
            }
            else
            {
                arguments.AddError($"level: '{text}' is not a log level. Use Trace, Debug, Information, Warning, Error or Critical.");
            }
        }

        return new LogsRequest(arguments.Long("since"), level, arguments.Text("category"), false);
    }
}
