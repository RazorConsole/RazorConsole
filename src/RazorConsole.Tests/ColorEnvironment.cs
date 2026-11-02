// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using RazorConsole.Testing;

namespace RazorConsole.Tests;

/// <summary>
/// Tests that compare colors must not depend on the developer's shell: <c>NO_COLOR</c> makes a RazorConsole app drop
/// its colors, and the other variables force or forbid them. A test removes them from the environment of the app it starts.
/// </summary>
internal static class ColorEnvironment
{
    private static readonly string[] Overrides = ["NO_COLOR", "FORCE_COLOR", "CLICOLOR", "CLICOLOR_FORCE"];

    /// <summary>
    /// Removes the color overrides from the environment the PTY app is started with.
    /// </summary>
    public static void Clear(PtyTerminalOptions options)
    {
        foreach (var name in Overrides)
        {
            options.Environment[name] = null;
        }
    }

    /// <summary>
    /// Removes the color overrides from a process the test starts, and so from everything that process starts.
    /// </summary>
    public static void Clear(ProcessStartInfo startInfo)
    {
        foreach (var name in Overrides)
        {
            startInfo.Environment.Remove(name);
        }
    }
}
