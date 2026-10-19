// Copyright (c) RazorConsole. All rights reserved.

using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static RazorConsole.Testing.Pty.WindowsNative;

namespace RazorConsole.Testing.Pty;

/// <summary>
/// A process attached to a Windows pseudoconsole (ConPTY).
/// </summary>
/// <remarks>
/// <para>
/// Handle inheritance: no handle created here is inheritable. The pipes are created without security attributes
/// (so they are not inheritable), and <c>CreateProcess</c> runs with <c>bInheritHandles = FALSE</c>: the child gets
/// its console through <c>PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE</c>, not through inherited handles. A concurrent
/// <c>CreateProcess</c> of another terminal (or of <see cref="System.Diagnostics.Process"/>) can therefore never pick
/// up this terminal's handles, and this terminal's child never picks up anyone else's.
/// </para>
/// <para>
/// Ctrl+C: a process started with <c>CREATE_NEW_PROCESS_GROUP</c> ignores Ctrl+C and its children inherit that bit,
/// so ConPTY's Ctrl+C would never reach the app. The bit is cleared just before <c>CreateProcess</c> and restored right
/// after, under a process-wide lock that covers only process creation.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows10.0.17763")]
internal sealed unsafe class WindowsPtyConnection : PtyConnection
{
    // Internal so that tests which change the Ctrl+C bit can hold it too.
    internal static readonly object s_processCreationLock = new();

    private readonly nint _pseudoConsole;
    private readonly SafeProcessHandle _process;
    private readonly SafeWaitHandle? _job;
    private readonly FileStream _input;
    private readonly FileStream _output;
    private readonly RegisteredWaitHandle _exitWait;
    private readonly ManualResetEvent _processEvent;
    private int _pseudoConsoleClosed;

    private WindowsPtyConnection(
        nint pseudoConsole,
        SafeProcessHandle process,
        int processId,
        SafeWaitHandle? job,
        SafeFileHandle input,
        SafeFileHandle output)
    {
        _pseudoConsole = pseudoConsole;
        _process = process;
        ProcessId = processId;
        _job = job;
        _input = new FileStream(input, FileAccess.Write, 0, isAsync: false);
        _output = new FileStream(output, FileAccess.Read, 0, isAsync: false);
        _processEvent = new ManualResetEvent(false) { SafeWaitHandle = new SafeWaitHandle(process.DangerousGetHandle(), ownsHandle: false) };
        _exitWait = ThreadPool.RegisterWaitForSingleObject(_processEvent, (_, _) => OnProcessExited(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    public override int ProcessId { get; }

    public static WindowsPtyConnection StartProcess(PtyStartInfo startInfo)
    {
        SafeFileHandle? inputRead = null, inputWrite = null, outputRead = null, outputWrite = null;
        nint pseudoConsole = 0;
        nint attributeList = 0;
        SafeWaitHandle? job = null;
        try
        {
            if (!CreatePipe(out inputRead, out inputWrite, 0, 0) || !CreatePipe(out outputRead, out outputWrite, 0, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "CreatePipe failed.");
            }

            var hr = CreatePseudoConsole(new Coord((short)startInfo.Width, (short)startInfo.Height), inputRead, outputWrite, 0, out pseudoConsole);
            Marshal.ThrowExceptionForHR(hr);

            // ConPTY keeps its own duplicates; ours would keep the pipes open after the console closes.
            inputRead.Dispose();
            outputWrite.Dispose();

            nint size = 0;
            InitializeProcThreadAttributeList(0, 1, 0, ref size);
            attributeList = Marshal.AllocHGlobal(size);
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref size))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "InitializeProcThreadAttributeList failed.");
            }

            if (!UpdateProcThreadAttribute(attributeList, 0, ProcThreadAttributePseudoConsole, pseudoConsole, sizeof(nint), 0, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "UpdateProcThreadAttribute failed.");
            }

            job = CreateKillOnCloseJob();
            var processInformation = CreateProcessInPseudoConsole(startInfo, attributeList);
            var process = new SafeProcessHandle(processInformation.Process, ownsHandle: true);
            try
            {
                // Created suspended so the job owns the whole tree before the app can start children.
                if (job is not null && !AssignProcessToJobObject(job, process))
                {
                    job.Dispose();
                    job = null;
                }

                ResumeThread(processInformation.Thread);
            }
            finally
            {
                CloseHandle(processInformation.Thread);
            }

            var connection = new WindowsPtyConnection(pseudoConsole, process, processInformation.ProcessId, job, inputWrite, outputRead);
            pseudoConsole = 0;
            job = null;
            return connection;
        }
        finally
        {
            if (attributeList != 0)
            {
                DeleteProcThreadAttributeList(attributeList);
                Marshal.FreeHGlobal(attributeList);
            }

            if (pseudoConsole != 0)
            {
                ClosePseudoConsole(pseudoConsole);
                inputWrite?.Dispose();
                outputRead?.Dispose();
            }

            inputRead?.Dispose();
            outputWrite?.Dispose();
            job?.Dispose();
        }
    }

    public override int Read(Span<byte> buffer)
    {
        try
        {
            return _output.Read(buffer);
        }
        catch (IOException)
        {
            return 0;
        }
        catch (ObjectDisposedException)
        {
            return 0;
        }
    }

    public override void Write(ReadOnlySpan<byte> data)
    {
        _input.Write(data);
        _input.Flush();
    }

    public override void Resize(int width, int height)
        => Marshal.ThrowExceptionForHR(ResizePseudoConsole(_pseudoConsole, new Coord((short)width, (short)height)));

    public override void Kill()
    {
        if (Exited.IsCompleted)
        {
            return;
        }

        if (_job is null || !TerminateJobObject(_job, -1))
        {
            TerminateProcess(_process, -1);
        }
    }

    public override void StopReading()
    {
        try
        {
            _input.Dispose();
        }
        catch (IOException)
        {
        }

        // Closing the pseudoconsole closes its end of the output pipe, so the reader sees end of file.
        // On older Windows versions this call blocks until the output is drained, which the reader keeps doing.
        if (Interlocked.Exchange(ref _pseudoConsoleClosed, 1) == 0)
        {
            ClosePseudoConsole(_pseudoConsole);
        }
    }

    public override void Dispose(bool readerStopped)
    {
        StopReading();
        if (readerStopped)
        {
            _output.Dispose();
        }

        _exitWait.Unregister(null);
        _job?.Dispose();
        if (Exited.IsCompleted)
        {
            _processEvent.Dispose();
            _process.Dispose();
        }
    }

    private void OnProcessExited()
    {
        if (GetExitCodeProcess(_process, out var exitCode))
        {
            SetExited(exitCode);
        }
        else
        {
            SetExitUnknown(new Win32Exception(Marshal.GetLastPInvokeError(), "GetExitCodeProcess failed."));
        }
    }

    private static SafeWaitHandle? CreateKillOnCloseJob()
    {
        var job = CreateJobObject(0, null);
        if (job.IsInvalid)
        {
            job.Dispose();
            return null;
        }

        var limits = new JobObjectExtendedLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose };
        if (!SetInformationJobObject(job, JobObjectExtendedLimitInformationClass, &limits, sizeof(JobObjectExtendedLimitInformation)))
        {
            job.Dispose();
            return null;
        }

        return job;
    }

    private static ProcessInformation CreateProcessInPseudoConsole(PtyStartInfo startInfo, nint attributeList)
    {
        var startup = new StartupInfoEx
        {
            Cb = sizeof(StartupInfoEx),
            AttributeList = attributeList,

            // Null standard handles: otherwise the child inherits the test runner's redirected stdio and bypasses the pseudoconsole.
            Flags = StartfUseStdHandles,
        };

        var commandLine = (BuildCommandLine(startInfo.FileName, startInfo.Arguments) + '\0').ToCharArray();
        var environment = BuildEnvironmentBlock(startInfo.Environment).ToCharArray();
        ProcessInformation processInformation;
        fixed (char* commandLinePointer = commandLine)
        fixed (char* environmentPointer = environment)
        {
            bool created;
            int error;
            lock (s_processCreationLock)
            {
                var ignoredCtrlC = ConsoleCtrlC.ClearIgnore();
                try
                {
                    created = CreateProcess(
                        null,
                        commandLinePointer,
                        0,
                        0,
                        inheritHandles: false,
                        ExtendedStartupInfoPresent | CreateUnicodeEnvironment | CreateSuspended,
                        environmentPointer,
                        startInfo.WorkingDirectory,
                        &startup,
                        &processInformation);
                    error = Marshal.GetLastPInvokeError();
                }
                finally
                {
                    ConsoleCtrlC.Restore(ignoredCtrlC);
                }
            }

            if (!created)
            {
                throw new Win32Exception(error, $"Failed to start '{startInfo.FileName}'.");
            }
        }

        return processInformation;
    }

    internal static string BuildCommandLine(string fileName, IReadOnlyList<string> arguments)
    {
        var builder = new StringBuilder();
        AppendArgument(builder, fileName);
        foreach (var argument in arguments)
        {
            builder.Append(' ');
            AppendArgument(builder, argument);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Quotes one argument so that <c>CommandLineToArgvW</c> and the C runtime parse it back unchanged.
    /// </summary>
    private static void AppendArgument(StringBuilder builder, string argument)
    {
        if (argument.Length > 0 && argument.AsSpan().IndexOfAny(" \t\n\v\"") < 0)
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        var backslashes = 0;
        foreach (var ch in argument)
        {
            if (ch == '\\')
            {
                backslashes++;
                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', backslashes * 2 + 1);
            }
            else
            {
                builder.Append('\\', backslashes);
            }

            backslashes = 0;
            builder.Append(ch);
        }

        builder.Append('\\', backslashes * 2);
        builder.Append('"');
    }

    private static string BuildEnvironmentBlock(IReadOnlyDictionary<string, string> environment)
    {
        var builder = new StringBuilder();
        foreach (var pair in environment.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
        }

        if (builder.Length == 0)
        {
            builder.Append('\0');
        }

        return builder.Append('\0').ToString();
    }

    /// <summary>
    /// Reads and changes the process-wide "ignore Ctrl+C" bit that <c>SetConsoleCtrlHandler(NULL, ...)</c> controls.
    /// </summary>
    /// <remarks>
    /// Windows has no API to read the bit. It is stored in <c>RTL_USER_PROCESS_PARAMETERS.ConsoleFlags</c> (bit 0),
    /// which is read through the PEB. The read is only trusted when it observes the clear that was just made;
    /// otherwise the previous state is treated as unknown and the bit is left cleared (the Windows default).
    /// </remarks>
    internal static class ConsoleCtrlC
    {
        public static bool ClearIgnore()
        {
            var before = TryReadConsoleFlags();
            SetConsoleCtrlHandler(0, false);
            var after = TryReadConsoleFlags();
            var readable = before is not null && after is not null && (after.Value & 1) == 0;
            return readable && (before!.Value & 1) != 0;
        }

        public static void Restore(bool ignoredCtrlC)
        {
            if (ignoredCtrlC)
            {
                SetConsoleCtrlHandler(0, true);
            }
        }

        internal static int? TryReadConsoleFlags()
        {
            ProcessBasicInformation information;
            if (NtQueryInformationProcess(-1, 0, &information, sizeof(ProcessBasicInformation), out _) != 0
                || information.PebBaseAddress == 0)
            {
                return null;
            }

            var (parametersOffset, consoleFlagsOffset) = nint.Size == 8 ? (0x20, 0x18) : (0x10, 0x14);
            var parameters = *(nint*)((byte*)information.PebBaseAddress + parametersOffset);
            return parameters == 0 ? null : Unsafe.ReadUnaligned<int>((byte*)parameters + consoleFlagsOffset);
        }
    }
}
