// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Testing;

namespace RazorConsole.Cli.Host;

/// <summary>
/// Picks the target framework <c>start --project</c> builds and runs. A project with several target frameworks makes
/// <c>dotnet run</c> ask which one to run, and inside a pseudoterminal that question is an interactive prompt that
/// never gets an answer, so the framework is always passed explicitly.
/// </summary>
internal static class ProjectFramework
{
    private const string RuntimeName = "Microsoft.NETCore.App";
    private static readonly TimeSpan EvaluationTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Chooses the highest of <paramref name="frameworks"/> that an installed runtime can run.
    /// </summary>
    /// <param name="frameworks">The project's target framework monikers, such as <c>net9.0</c>.</param>
    /// <param name="runtimes">The installed <c>Microsoft.NETCore.App</c> versions, such as <c>9.0.1</c>.</param>
    /// <param name="framework">The chosen moniker, exactly as the project spells it.</param>
    /// <param name="error">Why nothing can run: the project's frameworks and the installed runtimes.</param>
    /// <returns>Whether a framework was chosen.</returns>
    public static bool TryChoose(
        IReadOnlyList<string> frameworks,
        IReadOnlyList<string> runtimes,
        out string framework,
        out string error)
    {
        var installed = runtimes
            .Select(static version => TryParseVersion(version, out var parsed) ? parsed : ((int, int)?)null)
            .OfType<(int Major, int Minor)>()
            .ToList();

        // A runtime runs an app of the same major version and an equal or lower minor version.
        var runnable = frameworks
            .Select(static moniker => (Moniker: moniker, Version: TryParseMoniker(moniker, out var parsed) ? parsed : ((int, int)?)null))
            .Where(candidate => candidate.Version is { } version && installed.Any(runtime => runtime.Major == version.Item1 && runtime.Minor >= version.Item2))
            .OrderByDescending(static candidate => candidate.Version)
            .ToList();

        if (runnable.Count > 0)
        {
            framework = runnable[0].Moniker;
            error = string.Empty;
            return true;
        }

        framework = string.Empty;
        error = "None of the project's target frameworks can run on this machine. "
            + $"Target frameworks: {Describe(frameworks)}. "
            + $"Installed {RuntimeName} runtimes: {Describe(runtimes)}. "
            + "Install a matching runtime, or pass --framework.";
        return false;
    }

    /// <summary>
    /// Reads the installed <c>Microsoft.NETCore.App</c> versions from the output of <c>dotnet --list-runtimes</c>.
    /// </summary>
    public static IReadOnlyList<string> ParseRuntimes(string output)
    {
        var versions = new List<string>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && string.Equals(parts[0], RuntimeName, StringComparison.Ordinal))
            {
                versions.Add(parts[1]);
            }
        }

        return versions;
    }

    /// <summary>
    /// Splits the <c>TargetFramework</c> and <c>TargetFrameworks</c> properties of a project into its monikers.
    /// </summary>
    public static IReadOnlyList<string> ParseFrameworks(string? targetFramework, string? targetFrameworks)
        => (string.IsNullOrWhiteSpace(targetFrameworks) ? targetFramework ?? string.Empty : targetFrameworks)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Returns the framework to build and run <paramref name="project"/> with: <paramref name="requested"/> when given,
    /// the project's only framework, or the highest runnable one of several.
    /// </summary>
    public static async Task<string> ResolveAsync(string project, string? requested, string configuration, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested;
        }

        var dotnet = PtyTerminal.ResolveDotnetHost();
        var evaluation = await RunAsync(dotnet, ["msbuild", project, "-nologo", "-nodeReuse:false", $"-p:Configuration={configuration}", "-getProperty:TargetFramework,TargetFrameworks"], cancellationToken).ConfigureAwait(false);
        if (evaluation.ExitCode != 0)
        {
            throw new DiagnosticsProtocolException(
                DiagnosticsErrorCodes.InvalidParams,
                $"The target frameworks of '{project}' could not be read.{Environment.NewLine}{evaluation.Output.Trim()}");
        }

        var frameworks = ReadFrameworks(evaluation.Output);
        if (frameworks.Count == 0)
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, $"'{project}' does not define a target framework.");
        }

        if (frameworks.Count == 1)
        {
            return frameworks[0];
        }

        var listing = await RunAsync(dotnet, ["--list-runtimes"], cancellationToken).ConfigureAwait(false);
        if (!TryChoose(frameworks, ParseRuntimes(listing.Output), out var framework, out var error))
        {
            throw new DiagnosticsProtocolException(DiagnosticsErrorCodes.InvalidParams, error);
        }

        return framework;
    }

    private static IReadOnlyList<string> ReadFrameworks(string output)
    {
        try
        {
            // dotnet may print a banner or warnings before the JSON object.
            var start = output.IndexOf('{', StringComparison.Ordinal);
            using var document = JsonDocument.Parse(start < 0 ? output : output[start..]);
            var properties = document.RootElement.GetProperty("Properties");
            return ParseFrameworks(
                properties.TryGetProperty("TargetFramework", out var single) ? single.GetString() : null,
                properties.TryGetProperty("TargetFrameworks", out var several) ? several.GetString() : null);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return [];
        }
    }

    private static string Describe(IReadOnlyList<string> values)
        => values.Count == 0 ? "none" : string.Join(", ", values);

    // net8.0 and net9.0-windows both parse; netstandard2.0 and net48 are not runnable on .NET and do not.
    private static bool TryParseMoniker(string moniker, out (int Major, int Minor) version)
    {
        version = default;
        var core = moniker.Split('-', 2)[0];
        var digits = core.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase)
            ? core["netcoreapp".Length..]
            : core.StartsWith("net", StringComparison.OrdinalIgnoreCase) && !core.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
                ? core["net".Length..]
                : null;
        return digits is not null && digits.Contains('.', StringComparison.Ordinal) && TryParseVersion(digits, out version);
    }

    // 9.0.1 and 11.0.0-rc.1.26425.128 both give (major, minor).
    private static bool TryParseVersion(string text, out (int Major, int Minor) version)
    {
        version = default;
        var numbers = text.Split('-', 2)[0].Split('.');
        if (numbers.Length < 2
            || !int.TryParse(numbers[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(numbers[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        version = (major, minor);
        return true;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Returns when the process has exited, not when its output ends: a build server it started would hold the pipes.
        var (exitCode, output) = await PtySession.RunCapturedAsync(startInfo, EvaluationTimeout, $"'{fileName} {string.Join(' ', arguments)}'", cancellationToken).ConfigureAwait(false);
        return (exitCode, output);
    }
}
