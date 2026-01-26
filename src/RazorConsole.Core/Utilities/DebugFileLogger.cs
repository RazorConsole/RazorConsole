namespace RazorConsole.Core.Utilities;

internal static class DebugFileLogger
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RazorConsole",
        "focus-debug.log");

    private static readonly object _lock = new();

    static DebugFileLogger()
    {
        var dir = Path.GetDirectoryName(LogPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        // Clear log on startup
        File.WriteAllText(LogPath, $"=== Log started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\n");
    }

    public static void Log(string message)
    {
        lock (_lock)
        {
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
    }
}

