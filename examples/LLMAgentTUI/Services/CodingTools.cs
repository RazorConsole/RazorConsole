// Copyright (c) RazorConsole. All rights reserved.

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.AI;

namespace LLMAgentTUI.Services;

/// <summary>Small pi-inspired toolset. Approval is mandatory; shell execution is not sandboxed.</summary>
public sealed class CodingTools(string workingDirectory) : IDisposable
{
    private const int MaximumFileBytes = 2 * 1024 * 1024;
    private const int MaximumOutputBytes = 50 * 1024;
    private readonly string _root = Path.GetFullPath(workingDirectory);
    private readonly SemaphoreSlim _mutations = new(1, 1);

    public IList<AITool> Create()
        => [
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ReadAsync, "read")),
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(WriteAsync, "write")),
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(EditAsync, "edit")),
            new ApprovalRequiredAIFunction(AIFunctionFactory.Create(ShellAsync, OperatingSystem.IsWindows() ? "powershell" : "bash")),
        ];

    [Description("Read a UTF-8 text file in the workspace. Offset is a 1-based line number. Output is capped at 2000 lines or 50 KiB; continue with the reported next offset.")]
    public async Task<string> ReadAsync(string path, int offset = 1, int limit = 2000, CancellationToken cancellationToken = default)
    {
        if (offset < 1 || limit < 1 || limit > 2000)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must be positive; limit must be 1..2000.");
        }
        var text = await ReadTextAsync(Resolve(path), cancellationToken).ConfigureAwait(false);
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (offset > lines.Length)
        {
            return $"End of file ({lines.Length} lines).";
        }
        var output = new StringBuilder();
        var bytes = 0;
        var next = offset - 1;
        for (; next < lines.Length && next < offset - 1 + limit; next++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = $"{next + 1}: {lines[next]}\n";
            var count = Encoding.UTF8.GetByteCount(line);
            if (bytes + count > MaximumOutputBytes)
            {
                break;
            }
            output.Append(line);
            bytes += count;
        }
        if (next == offset - 1)
        {
            return "This line exceeds the 50 KiB output limit. Use an approved shell command to inspect a smaller portion.";
        }
        if (next < lines.Length)
        {
            output.Append($"\n[Truncated; continue with offset={next + 1}]");
        }
        return output.ToString();
    }

    [Description("Create or overwrite a UTF-8 text file inside the workspace. Creates missing parent directories. Requires approval.")]
    public async Task<string> WriteAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ValidateSize(content);
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var target = Resolve(path);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await WriteAtomicallyAsync(target, content, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            return $"Wrote {Encoding.UTF8.GetByteCount(content)} bytes to {path}.";
        }
        finally
        {
            _mutations.Release();
        }
    }

    [Description("Replace one exact, unique text block in a workspace UTF-8 file. No fuzzy matching. Read first and include enough context for a unique match. Requires approval.")]
    public async Task<string> EditAsync(string path, string oldText, string newText, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(oldText))
        {
            throw new ArgumentException("oldText must not be empty.", nameof(oldText));
        }
        await _mutations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var target = Resolve(path);
            var raw = await ReadBytesAsync(target, cancellationToken).ConfigureAwait(false);
            var bom = raw.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
            var content = new UTF8Encoding(false, true).GetString(raw, bom ? 3 : 0, raw.Length - (bom ? 3 : 0));
            var index = content.IndexOf(oldText, StringComparison.Ordinal);
            if (index < 0 || content.IndexOf(oldText, index + 1, StringComparison.Ordinal) >= 0)
            {
                throw new InvalidOperationException("oldText must match exactly once; the file was not changed.");
            }
            var result = content[..index] + newText + content[(index + oldText.Length)..];
            ValidateSize(result);
            cancellationToken.ThrowIfCancellationRequested();
            await WriteAtomicallyAsync(target, result, new UTF8Encoding(bom), cancellationToken).ConfigureAwait(false);
            return $"Edited {path}.\n- {Preview(oldText)}\n+ {Preview(newText)}";
        }
        finally
        {
            _mutations.Release();
        }
    }

    [Description("Execute a shell command in the workspace as the current OS user. NOT SANDBOXED: commands can access outside the workspace and the network. Requires explicit approval. Returns the last 50 KiB / 2000 lines of combined output. Timeout defaults to 120 seconds (maximum 600).")]
    public async Task<string> ShellAsync(string command, int timeoutSeconds = 120, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command) || timeoutSeconds is < 1 or > 600)
        {
            throw new ArgumentException("Provide a command and timeoutSeconds in 1..600.");
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        using var process = new Process
        {
            StartInfo = new(OperatingSystem.IsWindows() ? "pwsh" : "bash")
            {
                WorkingDirectory = _root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        foreach (var argument in OperatingSystem.IsWindows()
            ? new[] { "-NoProfile", "-NonInteractive", "-Command", command }
            : new[] { "--noprofile", "--norc", "-c", command })
        {
            process.StartInfo.ArgumentList.Add(argument);
        }
        timeout.Token.ThrowIfCancellationRequested();
        process.Start();
        process.StandardInput.Close();
        using var stop = timeout.Token.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        });
        var output = new StringBuilder();
        var truncated = false;
        async Task Drain(StreamReader reader)
        {
            var buffer = new char[4096];
            int count;
            while ((count = await reader.ReadAsync(buffer.AsMemory(), timeout.Token).ConfigureAwait(false)) > 0)
            {
                lock (output)
                {
                    output.Append(buffer, 0, count);
                    if (output.Length > MaximumOutputBytes)
                    {
                        output.Remove(0, output.Length - MaximumOutputBytes);
                        truncated = true;
                    }
                }
            }
        }
        var drain = Task.WhenAll(Drain(process.StandardOutput), Drain(process.StandardError));
        try
        {
            await Task.WhenAll(process.WaitForExitAsync(timeout.Token), drain).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Command exceeded {timeoutSeconds} seconds; process tree termination requested.");
        }
        var result = output.ToString();
        var lines = result.Split('\n');
        if (lines.Length > 2000)
        {
            result = string.Join('\n', lines.TakeLast(2000));
            truncated = true;
        }
        while (Encoding.UTF8.GetByteCount(result) > MaximumOutputBytes)
        {
            result = result[Math.Min(1024, result.Length)..];
            truncated = true;
        }
        return $"Exit code: {process.ExitCode}\n{(truncated ? "[Output truncated; showing tail]\n" : "")}{result}";
    }

    private string Resolve(string path)
    {
        var resolved = Path.GetFullPath(path, _root);
        var relative = Path.GetRelativePath(_root, resolved);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            throw new UnauthorizedAccessException("File tools are limited to the workspace.");
        }
        var current = _root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar).Prepend(""))
        {
            current = Path.Combine(current, segment);
            if (new FileInfo(current).LinkTarget is not null || new DirectoryInfo(current).LinkTarget is not null
                || ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
            {
                throw new UnauthorizedAccessException("File tools do not follow symbolic links or junctions.");
            }
        }
        return resolved;
    }

    private static void ValidateSize(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > MaximumFileBytes)
        {
            throw new IOException("Text exceeds the 2 MiB file limit.");
        }
    }

    private static async Task<string> ReadTextAsync(string path, CancellationToken cancellationToken)
    {
        var bytes = await ReadBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var bom = bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf });
        var content = new UTF8Encoding(false, true).GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
        if (content.Contains('\0'))
        {
            throw new IOException("Binary files are not supported by the text reader.");
        }
        return content;
    }

    private static async Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        if (stream.Length > MaximumFileBytes)
        {
            throw new IOException("File exceeds the 2 MiB limit. Use an approved shell command for larger files.");
        }
        var bytes = new byte[MaximumFileBytes + 1];
        var count = 0;
        int read;
        while (count < bytes.Length && (read = await stream.ReadAsync(bytes.AsMemory(count), cancellationToken).ConfigureAwait(false)) > 0)
        {
            count += read;
        }
        if (count > MaximumFileBytes)
        {
            throw new IOException("File grew beyond the 2 MiB limit.");
        }
        return bytes[..count];
    }

    private static string Preview(string text)
        => text.Length <= 4000 ? text : text[..4000] + "\n[Diff preview truncated; use read to inspect the full file]";

    private async Task WriteAtomicallyAsync(string target, string content, Encoding encoding, CancellationToken cancellationToken)
    {
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, $".llmagent-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, useAsync: true))
            {
                await file.WriteAsync(encoding.GetPreamble(), cancellationToken).ConfigureAwait(false);
                await file.WriteAsync(encoding.GetBytes(content), cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            if (!OperatingSystem.IsWindows() && File.Exists(target))
            {
                File.SetUnixFileMode(temporary, File.GetUnixFileMode(target));
            }
            cancellationToken.ThrowIfCancellationRequested();
            Resolve(target);
            File.Move(temporary, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    public void Dispose() => _mutations.Dispose();
}
