// Copyright (c) RazorConsole. All rights reserved.

// Prints every failed test from the test platform's logs, so the job log names it.
//
//   dotnet run scripts/show-failed-tests.cs [-- --directory <folder with the test logs>]
//
// `dotnet test` only prints how many tests failed in each module; the names, messages and stack traces are in
// artifacts/bin/RazorConsole.Tests/**/TestResults/*.log. Those logs are uploaded as an artifact, but a rerun replaces
// the artifact, so the failures of an earlier attempt are lost. This prints them into the attempt's own job log
// instead: one collapsible group per failure, an error annotation naming the test, and a list in the job summary.
// It never fails the step.

using System.Text;
using System.Text.RegularExpressions;

const int MaxLinesPerFailure = 120;

var directory = args.Length >= 2 && args[0] == "--directory"
    ? args[1]
    : Path.Combine("artifacts", "bin", "RazorConsole.Tests");
var onGitHub = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";

if (!Directory.Exists(directory))
{
    Console.WriteLine($"No test logs: {directory} does not exist.");
    return 0;
}

var logs = Directory.EnumerateFiles(directory, "*.log", SearchOption.AllDirectories)
    .Where(static path => Path.GetFileName(Path.GetDirectoryName(path)) == "TestResults")
    .Order(StringComparer.Ordinal)
    .ToList();
if (logs.Count == 0)
{
    Console.WriteLine($"No test logs under {directory}.");
    return 0;
}

var ansi = new Regex(@"\x1b\[[0-9;?]*[A-Za-z]", RegexOptions.Compiled);
var failedLine = new Regex(@"^failed (?<name>\S+)", RegexOptions.Compiled);
var summary = new StringBuilder();
var total = 0;

foreach (var log in logs)
{
    // The logs are UTF-16 with a byte order mark on Windows and UTF-8 elsewhere; ReadAllLines detects both.
    var lines = File.ReadAllLines(log).Select(line => ansi.Replace(line, string.Empty)).ToArray();
    var module = Path.GetFileNameWithoutExtension(log);
    for (var i = 0; i < lines.Length; i++)
    {
        var match = failedLine.Match(lines[i]);
        if (!match.Success)
        {
            continue;
        }

        total++;
        var name = match.Groups["name"].Value;
        var block = new List<string> { lines[i] };
        for (var j = i + 1; j < lines.Length && !EndsBlock(lines[j]); j++)
        {
            block.Add(lines[j]);
        }

        var message = block.Skip(1).Select(static line => line.Trim()).FirstOrDefault(static line => line.Length > 0) ?? string.Empty;
        if (onGitHub)
        {
            Console.WriteLine($"::error title=Test failed ({module})::{Escape(name)}: {Escape(message)}");
            Console.WriteLine($"::group::FAILED {name} ({module})");
        }
        else
        {
            Console.WriteLine($"=== FAILED {name} ({module})");
        }

        foreach (var line in block.Take(MaxLinesPerFailure))
        {
            Console.WriteLine(line);
        }

        if (block.Count > MaxLinesPerFailure)
        {
            Console.WriteLine($"... {block.Count - MaxLinesPerFailure} more lines in {log}");
        }

        if (onGitHub)
        {
            Console.WriteLine("::endgroup::");
        }

        summary.AppendLine($"- `{name}` ({module}): {message.Replace("|", "\\|", StringComparison.Ordinal)}");
    }
}

Console.WriteLine(total == 0
    ? $"No failed tests in {logs.Count} test log(s)."
    : $"{total} failed test(s) in {logs.Count} test log(s).");

var summaryPath = Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY");
if (total > 0 && !string.IsNullOrEmpty(summaryPath))
{
    File.AppendAllText(summaryPath, $"### Failed tests{Environment.NewLine}{Environment.NewLine}{summary}{Environment.NewLine}");
}

return 0;

// A failure's message and stack trace run until the next test result, progress line or run summary.
static bool EndsBlock(string line)
    => line.StartsWith("passed ", StringComparison.Ordinal)
        || line.StartsWith("failed ", StringComparison.Ordinal)
        || line.StartsWith("skipped ", StringComparison.Ordinal)
        || line.StartsWith("[+", StringComparison.Ordinal)
        || line.StartsWith("Test run summary", StringComparison.Ordinal);

// Workflow commands end at a newline, and % starts an escape.
static string Escape(string value)
    => value.Replace("%", "%25", StringComparison.Ordinal)
        .Replace("\r", "%0D", StringComparison.Ordinal)
        .Replace("\n", "%0A", StringComparison.Ordinal);
