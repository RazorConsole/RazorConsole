// Copyright (c) RazorConsole. All rights reserved.

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Globalization;
using RazorConsole.Cli.Operations;

namespace RazorConsole.Cli.Commands;

/// <summary>
/// The outcome of parsing a command line: a request to run, a built-in action (help, version) to invoke, or usage
/// errors. Exactly one of <see cref="Request"/>, <see cref="BuiltIn"/> and <see cref="Errors"/> is set.
/// </summary>
/// <param name="Options">The global options, as far as they could be read.</param>
/// <param name="Request">The request, when parsing succeeded.</param>
/// <param name="BuiltIn">The parse result to invoke for <c>--help</c> or <c>--version</c>.</param>
/// <param name="Errors">The usage errors; empty on success.</param>
internal sealed record CliParseOutcome(CliGlobalOptions Options, CliRequest? Request, ParseResult? BuiltIn, IReadOnlyList<string> Errors);

/// <summary>
/// A usage problem found after System.CommandLine accepted the tokens, such as two conflicting options.
/// </summary>
internal sealed class CliUsageException(string message) : Exception(message);

/// <summary>
/// The command tree of <c>razorconsole</c>. Verbs only turn tokens into <see cref="CliRequest"/> records; nothing
/// here talks to a session or the session host.
/// </summary>
internal sealed partial class CliCommandLine
{
    public const string SessionEnvironmentVariable = "RAZORCONSOLE_SESSION";

    private readonly Command _root;
    private readonly Command _start;
    private readonly Option<bool> _json;
    private readonly Option<string?> _session;
    private readonly Option<TimeSpan?> _timeout;
    private readonly Dictionary<Command, Func<VerbContext, CliRequest>> _factories = [];
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly string _currentDirectory;

    /// <param name="getEnvironmentVariable">Reads environment variables (<c>RAZORCONSOLE_SESSION</c>).</param>
    /// <param name="currentDirectory">The directory relative paths are resolved against. The host runs elsewhere, so
    /// requests carry full paths.</param>
    public CliCommandLine(Func<string, string?> getEnvironmentVariable, string currentDirectory)
    {
        _getEnvironmentVariable = getEnvironmentVariable;
        _currentDirectory = currentDirectory;

        _json = new Option<bool>("--json")
        {
            Description = "Write the result as one JSON object on stdout.",
            Recursive = true,
        };
        _session = new Option<string?>("--session", "-s")
        {
            Description = $"The session to use. Defaults to ${SessionEnvironmentVariable}, then the current session.",
            HelpName = "name",
            Recursive = true,
        };
        _session.Validators.Add(static result => ValidateSessionName(result, result.GetValueOrDefault<string?>()));
        _timeout = OptionalDurationOption("--timeout", "How long waits may take, such as 500ms, 2s or 1m. Defaults to 10s.");
        _timeout.Recursive = true;

        _root = new Command("razorconsole", "Drive and inspect RazorConsole apps: PTY sessions, input, screens, and the app's component tree.")
        {
            new HelpOption(),
            new VersionOption(),
            _json,
            _session,
            _timeout,
        };

        _start = AddVerbs();
    }

    public Command Root => _root;

    public CliParseOutcome Parse(IReadOnlyList<string> args)
    {
        // Everything after "--" belongs to the app that start launches, so it is never parsed as our own options.
        var parseResult = _root.Parse(args);
        IReadOnlyList<string> appArguments = [];
        var separator = IndexOfSeparator(args);
        if (separator >= 0)
        {
            var prefix = _root.Parse(args.Take(separator).ToArray());
            if (prefix.CommandResult.Command == _start)
            {
                parseResult = prefix;
                appArguments = args.Skip(separator + 1).ToArray();
            }
        }

        var errors = parseResult.Errors.Select(static error => error.Message).ToList();
        var options = ReadGlobalOptions(parseResult, args, separator, errors);

        if (parseResult.Action is not null and not ParseErrorAction)
        {
            return new CliParseOutcome(options, null, parseResult, []);
        }

        if (errors.Count == 0 && !_factories.ContainsKey(parseResult.CommandResult.Command))
        {
            errors.Add("Choose a command. Run 'razorconsole --help' to list them.");
        }

        if (errors.Count > 0)
        {
            return new CliParseOutcome(options, null, null, errors);
        }

        try
        {
            var request = _factories[parseResult.CommandResult.Command](new VerbContext(parseResult, appArguments));
            return new CliParseOutcome(options, request, null, []);
        }
        catch (CliUsageException exception)
        {
            return new CliParseOutcome(options, null, null, [exception.Message]);
        }
    }

    private CliGlobalOptions ReadGlobalOptions(ParseResult parseResult, IReadOnlyList<string> args, int separator, List<string> errors)
    {
        // Read --json from the tokens too, so even a command line that does not parse gets a JSON error.
        var json = args.Take(separator >= 0 ? separator : args.Count).Contains("--json", StringComparer.Ordinal)
            || (parseResult.Errors.Count == 0 && parseResult.GetValue(_json));
        if (parseResult.Errors.Count > 0)
        {
            return new CliGlobalOptions(json, null, CliGlobalOptions.DefaultTimeout);
        }

        var session = parseResult.GetValue(_session);
        if (session is null && _getEnvironmentVariable(SessionEnvironmentVariable) is { Length: > 0 } fromEnvironment)
        {
            if (CliValueParsers.TryValidateSessionName(fromEnvironment, out var error))
            {
                session = fromEnvironment;
            }
            else
            {
                errors.Add($"{SessionEnvironmentVariable}: {error}");
            }
        }

        return new CliGlobalOptions(json, session, parseResult.GetValue(_timeout) ?? CliGlobalOptions.DefaultTimeout);
    }

    private static int IndexOfSeparator(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "--")
            {
                return i;
            }
        }

        return -1;
    }

    private void Add(Command parent, Command command, Func<VerbContext, CliRequest> factory)
    {
        parent.Add(command);
        _factories[command] = factory;
    }

    private string FullPath(string path) => Path.GetFullPath(path, _currentDirectory);

    private static void ValidateSessionName(SymbolResult result, string? name)
    {
        if (name is not null && !CliValueParsers.TryValidateSessionName(name, out var error))
        {
            result.AddError(error);
        }
    }

    private static Option<TimeSpan?> OptionalDurationOption(string name, string description) => new(name)
    {
        Description = description,
        HelpName = "duration",
        CustomParser = ParseDuration,
    };

    private static TimeSpan? ParseDuration(ArgumentResult result)
    {
        if (result.Tokens.Count == 0)
        {
            return null;
        }

        if (!CliValueParsers.TryParseDuration(result.Tokens[^1].Value, out var value, out var error))
        {
            result.AddError(error);
            return null;
        }

        return value;
    }

    private static Option<int> IntOption(string name, int defaultValue, int minimum, int maximum, string description) => new(name)
    {
        Description = description,
        DefaultValueFactory = _ => defaultValue,
        CustomParser = result => (int?)ParseNumber(result, name, minimum, maximum) ?? defaultValue,
    };

    private static Option<int?> OptionalIntOption(string name, int minimum, string description) => new(name)
    {
        Description = description,
        CustomParser = result => (int?)ParseNumber(result, name, minimum, int.MaxValue),
    };

    private static Option<long?> OptionalLongOption(string name, string description, string helpName) => new(name)
    {
        Description = description,
        HelpName = helpName,
        CustomParser = result => ParseNumber(result, name, 0, long.MaxValue),
    };

    private static Argument<int> IntArgument(string name, int minimum, int maximum, string description) => new(name)
    {
        Description = description,
        CustomParser = result => (int)(ParseNumber(result, $"<{name}>", minimum, maximum) ?? 0),
    };

    /// <summary>
    /// Parses an integer and checks its range in one step. A validator could not do the range check, because reading
    /// the value of a token that is not a number throws instead of reporting.
    /// </summary>
    private static long? ParseNumber(ArgumentResult result, string name, long minimum, long maximum)
    {
        if (result.Tokens.Count == 0)
        {
            return null;
        }

        var text = result.Tokens[^1].Value;
        if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            result.AddError($"{name}: '{text}' is not a whole number.");
            return null;
        }

        if (number < minimum || number > maximum)
        {
            result.AddError(maximum is long.MaxValue or int.MaxValue
                ? $"{name} must be at least {minimum}."
                : $"{name} must be between {minimum} and {maximum}.");
            return null;
        }

        return number;
    }

    private static Argument<string?> SessionNameArgument(string description)
    {
        var argument = new Argument<string?>("name") { Description = description, Arity = ArgumentArity.ZeroOrOne };
        argument.Validators.Add(static result => ValidateSessionName(result, result.GetValueOrDefault<string?>()));
        return argument;
    }

    private static void RequireAtMostOne(params (string Name, bool Present)[] options)
    {
        var present = options.Where(static option => option.Present).Select(static option => option.Name).ToList();
        if (present.Count > 1)
        {
            throw new CliUsageException($"Use only one of {string.Join(", ", present)}.");
        }
    }

    /// <summary>
    /// What a verb factory reads from: the parse result, and the arguments after <c>--</c> (for <c>start</c>).
    /// </summary>
    private readonly record struct VerbContext(ParseResult ParseResult, IReadOnlyList<string> AppArguments)
    {
        public T? Get<T>(Option<T> option) => ParseResult.GetValue(option);

        public T? Get<T>(Argument<T> argument) => ParseResult.GetValue(argument);

        public bool IsGiven(Option option) => ParseResult.GetResult(option) is { Implicit: false };
    }
}
