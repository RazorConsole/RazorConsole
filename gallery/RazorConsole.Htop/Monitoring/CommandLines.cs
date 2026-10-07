// Copyright (c) RazorConsole. All rights reserved.

using System.Runtime.InteropServices;
using System.Text;

namespace RazorConsole.Htop.Monitoring;

/// <summary>Best-effort, cached full command lines for processes on Windows, Linux and macOS.</summary>
internal static unsafe partial class CommandLines
{
    private static readonly Dictionary<int, string> Cache = [];

    public static string Get(int pid)
    {
        if (pid <= 0)
        {
            return string.Empty;
        }

        if (Cache.TryGetValue(pid, out var cached))
        {
            return cached;
        }

        string text;
        try
        {
            text = OperatingSystem.IsWindows() ? ReadWindows(pid)
                : OperatingSystem.IsLinux() ? ReadLinux(pid)
                : OperatingSystem.IsMacOS() ? ReadMac(pid)
                : string.Empty;
        }
        catch
        {
            text = string.Empty;
        }

        if (Cache.Count > 4096)
        {
            Cache.Clear();
        }

        Cache[pid] = text;
        return text;
    }

    private static string ReadLinux(int pid)
    {
        var raw = File.ReadAllText($"/proc/{pid.ToString(System.Globalization.CultureInfo.InvariantCulture)}/cmdline");
        return raw.Replace('\0', ' ').Trim();
    }

    private static string ReadWindows(int pid)
    {
        const uint ProcessQueryLimitedInformation = 0x1000;
        const int ProcessCommandLineInformation = 60;
        var handle = OpenProcess(ProcessQueryLimitedInformation, 0, (uint)pid);
        if (handle == 0)
        {
            return string.Empty;
        }

        try
        {
            var buffer = new byte[8192];
            fixed (byte* p = buffer)
            {
                if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, p, (uint)buffer.Length, out _) != 0)
                {
                    return string.Empty;
                }

                var length = *(ushort*)p;
                var chars = *(char**)(p + sizeof(nint));
                return chars == null ? string.Empty : new string(chars, 0, length / 2);
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static string ReadMac(int pid)
    {
        var mib = stackalloc int[3];
        mib[0] = 1;
        mib[1] = 49;
        mib[2] = pid;
        nint size = 0;
        if (sysctl(mib, 3, null, &size, 0, 0) != 0 || size <= 4)
        {
            return string.Empty;
        }

        var buffer = new byte[size];
        fixed (byte* p = buffer)
        {
            if (sysctl(mib, 3, p, &size, 0, 0) != 0)
            {
                return string.Empty;
            }
        }

        var argc = BitConverter.ToInt32(buffer, 0);
        var i = 4;
        while (i < size && buffer[i] != 0)
        {
            i++;
        }

        while (i < size && buffer[i] == 0)
        {
            i++;
        }

        var parts = new List<string>();
        while (i < size && parts.Count < argc)
        {
            var start = i;
            while (i < size && buffer[i] != 0)
            {
                i++;
            }

            parts.Add(Encoding.UTF8.GetString(buffer, start, i - start));
            i++;
        }

        return string.Join(' ', parts);
    }

    [LibraryImport("kernel32.dll")]
    private static partial nint OpenProcess(uint access, int inherit, uint pid);

    [LibraryImport("kernel32.dll")]
    private static partial int CloseHandle(nint handle);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(nint handle, int infoClass, byte* info, uint length, out uint returned);

    [LibraryImport("libc", EntryPoint = "sysctl")]
    private static partial int sysctl(int* name, uint namelen, byte* oldp, nint* oldlenp, nint newp, nint newlen);
}
