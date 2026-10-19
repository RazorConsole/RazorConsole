// Copyright (c) RazorConsole. All rights reserved.

using System.Runtime.InteropServices;

namespace RazorConsole.Testing.Pty;

internal static class PtyPlatform
{
    private static readonly Lazy<bool> s_isSupported = new(DetectSupport);

    public static bool IsSupported => s_isSupported.Value;

    public static PlatformNotSupportedException CreateNotSupportedException()
    {
        if (OperatingSystem.IsWindows())
        {
            return new PlatformNotSupportedException(
                $"PtyTerminal requires the Windows pseudoconsole (ConPTY), which is available on Windows 10 version 1809 (build 17763) and later. This system is {Environment.OSVersion.VersionString}.");
        }

        return new PlatformNotSupportedException(
            $"PtyTerminal supports Windows 10 version 1809 or later, Linux, and macOS. This platform is {RuntimeInformation.OSDescription}.");
    }

    private static bool DetectSupport()
    {
        if (OperatingSystem.IsWindows())
        {
            return OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763)
                && NativeLibrary.TryLoad("kernel32.dll", out var kernel32)
                && NativeLibrary.TryGetExport(kernel32, "CreatePseudoConsole", out _);
        }

        return OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
    }
}
