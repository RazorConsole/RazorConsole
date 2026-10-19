// Copyright (c) RazorConsole. All rights reserved.

namespace RazorConsole.Testing;

/// <summary>
/// Options for starting a <see cref="PtyTerminal"/>.
/// </summary>
public sealed class PtyTerminalOptions
{
    /// <summary>
    /// Gets or sets the program to run: a path, or a name that is looked up on <c>PATH</c>.
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// Gets the command-line arguments. Each argument is passed as-is; quoting is handled by the host.
    /// </summary>
    public IList<string> Arguments { get; } = new List<string>();

    /// <summary>
    /// Gets or sets the working directory of the process, or <see langword="null"/> for the test process's current directory.
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Gets environment variable overrides applied on top of the test process's environment.
    /// A <see langword="null"/> value removes the variable.
    /// </summary>
    public IDictionary<string, string?> Environment { get; } = new Dictionary<string, string?>(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the terminal width in columns. Defaults to 80.
    /// </summary>
    public int Width { get; set; } = 80;

    /// <summary>
    /// Gets or sets the terminal height in rows. Defaults to 24.
    /// </summary>
    public int Height { get; set; } = 24;

    /// <summary>
    /// Gets or sets the <c>TERM</c> value for the process on Linux and macOS. Defaults to <c>xterm-256color</c>.
    /// Ignored on Windows, where ConPTY defines the terminal. An explicit <c>TERM</c> in <see cref="Environment"/> wins.
    /// </summary>
    public string TerminalType { get; set; } = "xterm-256color";

    /// <summary>
    /// Gets or sets the timeout used by the wait methods when none is passed. Defaults to 10 seconds.
    /// </summary>
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Gets or sets a value indicating whether the terminal answers queries the way a real terminal does:
    /// cursor position (<c>CSI 6 n</c>), device attributes (<c>CSI c</c>, <c>CSI &gt; c</c>), mode reports
    /// (<c>CSI ? Ps $ p</c>) and the text area size (<c>CSI 18 t</c>). Defaults to <see langword="true"/>.
    /// </summary>
    public bool RespondToTerminalQueries { get; set; } = true;
}
