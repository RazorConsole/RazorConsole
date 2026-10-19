// Copyright (c) RazorConsole. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace RazorConsole.Testing.Pty;

[SupportedOSPlatform("windows")]
internal static unsafe partial class WindowsNative
{
    public const uint ExtendedStartupInfoPresent = 0x00080000;
    public const uint CreateUnicodeEnvironment = 0x00000400;
    public const uint CreateSuspended = 0x00000004;
    public const int StartfUseStdHandles = 0x00000100;
    public const nint ProcThreadAttributePseudoConsole = 0x00020016;
    public const int JobObjectExtendedLimitInformationClass = 9;
    public const uint JobObjectLimitKillOnJobClose = 0x00002000;

    private const string Kernel32 = "kernel32.dll";

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreatePipe(out SafeFileHandle readPipe, out SafeFileHandle writePipe, nint pipeAttributes, int size);

    [LibraryImport(Kernel32)]
    public static partial int CreatePseudoConsole(Coord size, SafeFileHandle input, SafeFileHandle output, uint flags, out nint pseudoConsole);

    [LibraryImport(Kernel32)]
    public static partial int ResizePseudoConsole(nint pseudoConsole, Coord size);

    [LibraryImport(Kernel32)]
    public static partial void ClosePseudoConsole(nint pseudoConsole);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InitializeProcThreadAttributeList(nint attributeList, int attributeCount, int flags, ref nint size);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateProcThreadAttribute(
        nint attributeList, uint flags, nint attribute, nint value, nint size, nint previousValue, nint returnSize);

    [LibraryImport(Kernel32)]
    public static partial void DeleteProcThreadAttributeList(nint attributeList);

    [LibraryImport(Kernel32, EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateProcess(
        string? applicationName,
        char* commandLine,
        nint processAttributes,
        nint threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        char* environment,
        string? currentDirectory,
        StartupInfoEx* startupInfo,
        ProcessInformation* processInformation);

    [LibraryImport(Kernel32, SetLastError = true)]
    public static partial uint ResumeThread(nint thread);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint handle);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetExitCodeProcess(SafeProcessHandle process, out int exitCode);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateProcess(SafeProcessHandle process, int exitCode);

    [LibraryImport(Kernel32, EntryPoint = "CreateJobObjectW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial SafeWaitHandle CreateJobObject(nint jobAttributes, string? name);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetInformationJobObject(SafeWaitHandle job, int informationClass, void* information, int length);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AssignProcessToJobObject(SafeWaitHandle job, SafeProcessHandle process);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TerminateJobObject(SafeWaitHandle job, int exitCode);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetConsoleCtrlHandler(nint handler, [MarshalAs(UnmanagedType.Bool)] bool add);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(
        nint process, int informationClass, ProcessBasicInformation* information, int length, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    public readonly struct Coord(short x, short y)
    {
        public readonly short X = x;
        public readonly short Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct StartupInfoEx
    {
        public int Cb;
        public nint Reserved;
        public nint Desktop;
        public nint Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2Count;
        public nint Reserved2;
        public nint StdInput;
        public nint StdOutput;
        public nint StdError;
        public nint AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessInformation
    {
        public nint Process;
        public nint Thread;
        public int ProcessId;
        public int ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JobObjectExtendedLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessBasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nuint AffinityMask;
        public nint BasePriority;
        public nuint UniqueProcessId;
        public nuint InheritedFromUniqueProcessId;
    }
}
