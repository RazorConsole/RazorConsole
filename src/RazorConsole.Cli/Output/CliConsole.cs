// Copyright (c) RazorConsole. All rights reserved.

using System.Text;

namespace RazorConsole.Cli.Output;

/// <summary>
/// stdout and stderr as UTF-8. Human-readable output carries box-drawing characters, ● and arrows, which the console's
/// code page often cannot encode (cp866 or cp437 on Windows turn them into <c>?</c>). The JSON output is ASCII-escaped
/// and does not depend on this.
/// </summary>
internal sealed class CliConsole : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Encoding? _restoreEncoding;

    private CliConsole(TextWriter output, TextWriter error, Encoding? restoreEncoding)
    {
        Output = output;
        Error = error;
        _restoreEncoding = restoreEncoding;
    }

    public TextWriter Output { get; }

    public TextWriter Error { get; }

    /// <summary>
    /// A writer that encodes as UTF-8 without a byte order mark and flushes every write, so streamed output
    /// (<c>logs --follow</c>) reaches a pipe at once.
    /// </summary>
    public static TextWriter CreateWriter(Stream stream)
        => new StreamWriter(stream, Utf8, bufferSize: 4096, leaveOpen: true) { AutoFlush = true };

    /// <summary>
    /// Opens the process's stdout and stderr. A redirected stream (a pipe, a file, nushell capturing output) gets the
    /// UTF-8 bytes directly. A console window is switched to UTF-8 for the run and put back afterwards.
    /// </summary>
    public static CliConsole Open()
    {
        Encoding? restore = null;
        if (!Console.IsOutputRedirected || !Console.IsErrorRedirected)
        {
            restore = TrySwitchConsoleToUtf8();
        }

        var output = Console.IsOutputRedirected ? CreateWriter(Console.OpenStandardOutput()) : Console.Out;
        var error = Console.IsErrorRedirected ? CreateWriter(Console.OpenStandardError()) : Console.Error;
        return new CliConsole(output, error, restore);
    }

    public void Dispose()
    {
        Output.Flush();
        Error.Flush();
        if (_restoreEncoding is not null)
        {
            try
            {
                Console.OutputEncoding = _restoreEncoding;
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // The console keeps UTF-8, which is harmless.
            }
        }
    }

    // Returns the encoding to restore, or null when nothing was changed.
    private static Encoding? TrySwitchConsoleToUtf8()
    {
        try
        {
            var original = Console.OutputEncoding;
            if (original.CodePage == Utf8.CodePage)
            {
                return null;
            }

            Console.OutputEncoding = Utf8;
            return original;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
