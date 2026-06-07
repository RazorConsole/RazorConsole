// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;

namespace RazorConsole.Core.Utilities;

public static class ExtendedCapabilities
{
    /// <summary>
    /// Windows conhost
    /// </summary>
    public static bool IsConhost { get; }
    /// <summary>
    /// NEL is not supported on older versions of conhost -> legacy
    /// </summary>
    public static bool IsLegacyConhost { get; }

    static ExtendedCapabilities()
    {
        (IsConhost, IsLegacyConhost) = DetectConhost();
        Debug.WriteLine($"Conhost: {IsConhost}");
        Debug.WriteLine($"LegacyConhost: {IsLegacyConhost}");
    }

    private static (bool isConhost, bool isLegacyConhost) DetectConhost()
    {
        if (!OperatingSystem.IsWindows())
        {
            return (false, false);
        }
        try
        {
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WT_Session")))
            {
                // Windows terminal ('WT_Session' is not always set!)
                return (false, false);
            }

            var initialCursor = Console.GetCursorPosition();

            // Check if NEL moved the cursor to the next line and
            // also if the viewport gets scrolled when we move past the last line
            Console.SetCursorPosition(0, 0);
            Console.Write(string.Concat(Enumerable.Repeat(AnsiSequences.NEL(), Console.WindowHeight)));
            var cursorTop = Console.CursorTop;
            var windowTop = Console.WindowTop;

            Console.SetCursorPosition(initialCursor.Left, initialCursor.Top); // Move back to position before check

            // NEL is not supported on older versions of conhost -> nothing happend? -> legacy
            var nelSupported = cursorTop != 0;

            // If conhost is used this will be 1, since we scrolled the viewport (assuming NEL is supported)
            var isConhost = !nelSupported || windowTop == 1;

            return (isConhost, !nelSupported);
        }
        catch (Exception)
        {
            // On conhost none of the Console.* operations would throw
            return (false, false);
        }
    }
}
