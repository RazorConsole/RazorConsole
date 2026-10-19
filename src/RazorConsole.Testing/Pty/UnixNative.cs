// Copyright (c) RazorConsole. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace RazorConsole.Testing.Pty;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal static unsafe partial class UnixNative
{
    public const int ORdWr = 2;
    public const int EIntr = 4;
    public const int EChild = 10;
    public const int SigKill = 9;
    public const short PollIn = 0x1;
    public const int FSetFd = 2;
    public const int FdCloExec = 1;
    public const int XOk = 1;
    public const int ENoEnt = 2;

    private const string LibC = "libc";

    public static int ONoCtty => OperatingSystem.IsMacOS() ? 0x20000 : 0x100;

    /// <summary>
    /// Gets <c>O_CLOEXEC</c> for <c>posix_openpt</c>, or 0 where <c>posix_openpt</c> does not accept it (macOS).
    /// </summary>
    public static int OCloExecForOpenPt => OperatingSystem.IsMacOS() ? 0 : 0x80000;

    /// <summary>
    /// Gets <c>O_CLOEXEC</c> for <c>open</c>, which sets it atomically on both Linux and macOS.
    /// </summary>
    public static int OCloExec => OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000;

    public static short PosixSpawnSetSid => OperatingSystem.IsMacOS() ? (short)0x400 : (short)0x80;

    public const short PosixSpawnSetSigDef = 0x04;
    public const short PosixSpawnSetSigMask = 0x08;

    public static nuint TiocSWinSz => OperatingSystem.IsMacOS() ? 0x80087467 : 0x5414;

    /// <summary>
    /// Gets the signals whose dispositions are reset to default in the child: HUP, INT, QUIT, PIPE, TERM, TSTP, TTIN, TTOU, WINCH.
    /// The .NET runtime ignores SIGPIPE, and ignored dispositions survive <c>exec</c>.
    /// </summary>
    public static int[] DefaultSignals => OperatingSystem.IsMacOS()
        ? [1, 2, 3, 13, 15, 18, 21, 22, 28]
        : [1, 2, 3, 13, 15, 20, 21, 22, 28];

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int posix_openpt(int flags);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int grantpt(int fd);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int unlockpt(int fd);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int ptsname_r(int fd, byte* buffer, nuint length);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial byte* ptsname(int fd);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int fcntl(int fd, int command, int argument);

    [LibraryImport(LibC)]
    public static partial int posix_spawn_file_actions_init(void* actions);

    [LibraryImport(LibC)]
    public static partial int posix_spawn_file_actions_destroy(void* actions);

    [LibraryImport(LibC)]
    public static partial int posix_spawn_file_actions_addopen(void* actions, int fd, byte* path, int flags, uint mode);

    [LibraryImport(LibC)]
    public static partial int posix_spawn_file_actions_adddup2(void* actions, int fd, int newFd);

    [LibraryImport(LibC)]
    public static partial int posix_spawn_file_actions_addclose(void* actions, int fd);

    [LibraryImport(LibC)]
    public static partial int posix_spawnattr_init(void* attributes);

    [LibraryImport(LibC)]
    public static partial int posix_spawnattr_destroy(void* attributes);

    [LibraryImport(LibC)]
    public static partial int posix_spawnattr_setflags(void* attributes, short flags);

    [LibraryImport(LibC)]
    public static partial int posix_spawnattr_setsigmask(void* attributes, void* set);

    [LibraryImport(LibC)]
    public static partial int posix_spawnattr_setsigdefault(void* attributes, void* set);

    [LibraryImport(LibC)]
    public static partial int sigemptyset(void* set);

    [LibraryImport(LibC)]
    public static partial int sigaddset(void* set, int signal);

    [LibraryImport(LibC)]
    public static partial int posix_spawnp(int* pid, byte* file, void* actions, void* attributes, byte** argv, byte** envp);

    [LibraryImport(LibC, EntryPoint = "ioctl", SetLastError = true)]
    public static partial int ioctl_winsize(int fd, nuint request, WinSize* size);

    /// <summary>
    /// <c>ioctl</c> is variadic. On Apple arm64 variadic arguments are passed on the stack, so six dummy register
    /// arguments push the <c>winsize</c> pointer into the first stack slot, where the callee reads it.
    /// </summary>
    [LibraryImport(LibC, EntryPoint = "ioctl", SetLastError = true)]
    public static partial int ioctl_winsize_apple_arm64(
        int fd, nuint request, nint unused1, nint unused2, nint unused3, nint unused4, nint unused5, nint unused6, WinSize* size);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial nint read(int fd, byte* buffer, nuint count);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial nint write(int fd, byte* buffer, nuint count);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int poll(PollFd* fds, nuint count, int timeout);

    // open is variadic; without O_CREAT no mode is passed, so the fixed two-argument signature is correct on every ABI.
    [LibraryImport(LibC, SetLastError = true)]
    public static partial int open(byte* path, int flags);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int close(int fd);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int waitpid(int pid, int* status, int options);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int kill(int pid, int signal);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int tcgetpgrp(int fd);

    [LibraryImport(LibC, SetLastError = true)]
    public static partial int access(byte* path, int mode);

    public static int SetWindowSize(int fd, int width, int height)
    {
        var size = new WinSize { Rows = (ushort)height, Columns = (ushort)width };
        return OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? ioctl_winsize_apple_arm64(fd, TiocSWinSz, 0, 0, 0, 0, 0, 0, &size)
            : ioctl_winsize(fd, TiocSWinSz, &size);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WinSize
    {
        public ushort Rows;
        public ushort Columns;
        public ushort XPixels;
        public ushort YPixels;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short ReturnedEvents;
    }
}
