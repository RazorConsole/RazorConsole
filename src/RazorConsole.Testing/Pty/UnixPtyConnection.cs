// Copyright (c) RazorConsole. All rights reserved.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using static RazorConsole.Testing.Pty.UnixNative;

namespace RazorConsole.Testing.Pty;

/// <summary>
/// A process attached to a POSIX pseudoterminal, started with <c>posix_spawnp</c> (no <c>fork</c> in the test process).
/// </summary>
/// <remarks>
/// <para>
/// The child runs in a new session (<c>POSIX_SPAWN_SETSID</c>) with the PTY slave as its controlling terminal, so the
/// kernel delivers Ctrl+C as SIGINT and a resize as SIGWINCH to it. A session leader acquires the controlling terminal
/// by opening a terminal without <c>O_NOCTTY</c>. glibc and musl call <c>setsid</c> before the file actions, so on Linux
/// opening the slave as fd 0 does it. macOS runs the file actions before <c>setsid</c>, so that open happens in the
/// test process's session and no controlling terminal is acquired. On macOS the child is therefore started as
/// <c>/bin/bash</c>, which, already the session leader, reopens the slave (acquiring it as the controlling
/// terminal) and execs the program in the same process. bash opens a
/// <c>&lt;&gt;</c> redirection without <c>O_NOCTTY</c>; zsh, which <c>/bin/sh</c> may be, adds it, so bash is named
/// explicitly.
/// </para>
/// <para>
/// bash never sees the variables it would act on (<c>BASH_ENV</c>, <c>SHELLOPTS</c>, imported functions, ...), and
/// the program still gets exactly the caller's environment, without putting it in argv; see <see cref="BashLaunch"/>.
/// bash's <c>-p</c> is not enough: it still exports its own <c>SHELLOPTS</c> and <c>SHLVL</c> to the program.
/// </para>
/// <para>
/// End of output: reading the master fails with <c>EIO</c> once no slave descriptor is open, and on Linux that can be
/// seen before <c>waitpid</c> reports the exit. The parent therefore keeps its own slave descriptor (<c>O_NOCTTY</c>,
/// so the test process never acquires the terminal) until the child is reaped, then closes it: the output ends only
/// after everything the child wrote has been read and the exit status is known.
/// </para>
/// <para>
/// fd inheritance: on Linux the master is opened with <c>O_CLOEXEC</c> atomically, so no other child can inherit it.
/// macOS <c>posix_openpt</c> does not accept <c>O_CLOEXEC</c>; there <c>FD_CLOEXEC</c> is set right after opening, and
/// opening plus spawning run under a process-wide lock so another terminal's spawn cannot happen in between.
/// The parent's slave descriptor is opened with <c>O_CLOEXEC</c>, which <c>open</c> sets atomically on both systems.
/// </para>
/// <para>
/// Window size: macOS rejects terminal ioctls such as <c>TIOCSWINSZ</c> on a new master with <c>ENOTTY</c> until the
/// slave has been opened once. The parent's slave descriptor is therefore opened before the initial size is set on
/// the master.
/// </para>
/// <para>
/// Exit: a dedicated thread runs <c>waitpid(pid)</c> for this child only.
/// </para>
/// </remarks>
[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
internal sealed unsafe class UnixPtyConnection : PtyConnection
{
    private const int PollTimeoutMilliseconds = 50;
    private const int StopDrainMilliseconds = 500;

    private static readonly object s_spawnLock = new();
    private static readonly nint s_addChdir = ResolveAddChdir();

    private readonly int _master;
    private int _slave;
    private volatile bool _stopReading;
    private long _stopDrainDeadline;
    private int _disposed;

    private UnixPtyConnection(int master, int slave, int processId)
    {
        _master = master;
        _slave = slave;
        ProcessId = processId;
        var waiter = new Thread(WaitForExit) { IsBackground = true, Name = $"PtyTerminal waitpid {processId}" };
        waiter.Start();
    }

    public override int ProcessId { get; }

    /// <summary>
    /// Gets the terminal's foreground process group, which receives the signals the line discipline generates.
    /// </summary>
    public int ForegroundProcessGroup
    {
        get
        {
            var group = tcgetpgrp(_master);
            return group < 0 ? throw new IOException($"tcgetpgrp failed (errno {Marshal.GetLastPInvokeError()}).") : group;
        }
    }

    public static UnixPtyConnection StartProcess(PtyStartInfo startInfo)
    {
        if (OperatingSystem.IsMacOS())
        {
            lock (s_spawnLock)
            {
                return StartCore(startInfo);
            }
        }

        return StartCore(startInfo);
    }

    public override int Read(Span<byte> buffer)
    {
        var poll = new PollFd { Fd = _master, Events = PollIn };
        fixed (byte* pointer = buffer)
        {
            while (true)
            {
                // After StopReading, return what is already buffered (bounded, in case a grandchild keeps writing).
                var stopping = _stopReading;
                if (stopping && Environment.TickCount64 >= Interlocked.Read(ref _stopDrainDeadline))
                {
                    return 0;
                }

                var ready = UnixNative.poll(&poll, 1, stopping ? 0 : PollTimeoutMilliseconds);
                if (ready == 0 && stopping)
                {
                    return 0;
                }

                if (ready == 0 || (ready < 0 && Marshal.GetLastPInvokeError() == EIntr))
                {
                    continue;
                }

                if (ready < 0)
                {
                    return 0;
                }

                var count = read(_master, pointer, (nuint)buffer.Length);
                if (count > 0)
                {
                    return (int)count;
                }

                if (count < 0 && Marshal.GetLastPInvokeError() == EIntr)
                {
                    continue;
                }

                // 0 or EIO: every slave descriptor is closed.
                return 0;
            }
        }
    }

    public override void Write(ReadOnlySpan<byte> data)
    {
        fixed (byte* pointer = data)
        {
            var offset = 0;
            while (offset < data.Length)
            {
                var count = write(_master, pointer + offset, (nuint)(data.Length - offset));
                if (count < 0)
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error == EIntr)
                    {
                        continue;
                    }

                    throw new IOException($"Writing to the pseudoterminal failed (errno {error}).");
                }

                offset += (int)count;
            }
        }
    }

    public override void Resize(int width, int height)
    {
        // The kernel sends SIGWINCH to the foreground process group.
        SetWindowSizeOrThrow(_master, "master", width, height);
    }

    public override void Kill()
    {
        if (!Exited.IsCompleted)
        {
            // The child is a session and process group leader; kill the whole group.
            kill(-ProcessId, SigKill);
        }
    }

    public override void StopReading()
    {
        if (!_stopReading)
        {
            Interlocked.CompareExchange(ref _stopDrainDeadline, Environment.TickCount64 + StopDrainMilliseconds, 0);
            _stopReading = true;
        }
    }

    public override void Dispose(bool readerStopped)
    {
        StopReading();
        CloseSlave();
        if (readerStopped && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            close(_master);
        }
    }

    private static UnixPtyConnection StartCore(PtyStartInfo startInfo)
    {
        var master = posix_openpt(ORdWr | ONoCtty | OCloExecForOpenPt);
        if (master < 0)
        {
            throw new IOException($"posix_openpt failed (errno {Marshal.GetLastPInvokeError()}).");
        }

        try
        {
            if (OCloExecForOpenPt == 0 && fcntl(master, FSetFd, FdCloExec) != 0)
            {
                throw new IOException($"fcntl(FD_CLOEXEC) failed (errno {Marshal.GetLastPInvokeError()}).");
            }

            if (grantpt(master) != 0 || unlockpt(master) != 0)
            {
                throw new IOException($"grantpt/unlockpt failed (errno {Marshal.GetLastPInvokeError()}).");
            }

            var slavePath = GetSlavePath(master);
            int slave;
            fixed (byte* path = slavePath)
            {
                slave = open(path, ORdWr | ONoCtty | OCloExec);
            }

            if (slave < 0)
            {
                throw new IOException($"Opening the pseudoterminal slave failed (errno {Marshal.GetLastPInvokeError()}).");
            }

            try
            {
                // After the slave open: until then macOS answers ENOTTY.
                SetWindowSizeOrThrow(master, "master", startInfo.Width, startInfo.Height);
                var processId = Spawn(startInfo, master, slavePath);
                return new UnixPtyConnection(master, slave, processId);
            }
            catch
            {
                close(slave);
                throw;
            }
        }
        catch
        {
            close(master);
            throw;
        }
    }

    private static void SetWindowSizeOrThrow(int fd, string role, int width, int height)
    {
        if (SetWindowSize(fd, width, height) != 0)
        {
            throw new IOException(
                $"ioctl(TIOCSWINSZ 0x{TiocSWinSz:x}) on the {role} fd {fd} failed (errno {Marshal.GetLastPInvokeError()}).");
        }
    }

    private static byte[] GetSlavePath(int master)
    {
        var buffer = stackalloc byte[1024];
        if (ptsname_r(master, buffer, 1024) == 0)
        {
            return new ReadOnlySpan<byte>(buffer, IndexOfNul(buffer, 1024) + 1).ToArray();
        }

        // ptsname_r is missing on older macOS; there spawning is serialized, so the static buffer is not shared.
        var name = ptsname(master);
        if (name is null)
        {
            throw new IOException($"ptsname failed (errno {Marshal.GetLastPInvokeError()}).");
        }

        return new ReadOnlySpan<byte>(name, IndexOfNul(name, 1024) + 1).ToArray();
    }

    private static int IndexOfNul(byte* text, int maximum)
    {
        var length = new ReadOnlySpan<byte>(text, maximum).IndexOf((byte)0);
        return length < 0 ? throw new IOException("The pseudoterminal name is too long.") : length;
    }

    private static int Spawn(PtyStartInfo startInfo, int master, byte[] slavePath)
    {
        var fileName = startInfo.FileName;
        var arguments = new List<string>(startInfo.Arguments.Count + 6);
        var environment = startInfo.Environment.Select(pair => pair.Key + "=" + pair.Value).ToArray();
        var changeDirectory = startInfo.WorkingDirectory is not null && s_addChdir == 0;
        if (OperatingSystem.IsMacOS())
        {
            // bash attaches the terminal, then execs the program through env (see the remarks). The program is
            // resolved here, as posix_spawnp would, so a missing program still fails the start instead of exiting
            // with 127.
            var program = ResolveProgram(startInfo);
            if (program.Contains('='))
            {
                throw new IOException($"Failed to start '{startInfo.FileName}': on macOS the program path cannot contain '='.");
            }

            var slave = Encoding.UTF8.GetString(slavePath, 0, slavePath.Length - 1);
            var launch = BashLaunch.Build(program, slave, changeDirectory ? startInfo.WorkingDirectory : null, startInfo.Environment);
            arguments.AddRange(launch.Arguments);
            fileName = BashLaunch.Bash;
            environment = launch.Environment;
        }
        else if (changeDirectory)
        {
            // No posix_spawn_file_actions_addchdir_np: change directory in a shell. Never chdir the test process.
            arguments.AddRange(["/bin/sh", "-c", "cd -- \"$0\" && exec \"$@\"", startInfo.WorkingDirectory!, fileName]);
            fileName = "/bin/sh";
        }
        else
        {
            arguments.Add(fileName);
        }

        arguments.AddRange(startInfo.Arguments);

        // posix_spawn_file_actions_t / posix_spawnattr_t / sigset_t are at most a few hundred bytes on Linux and pointers on macOS.
        var actions = stackalloc byte[1024];
        var attributes = stackalloc byte[1024];
        var mask = stackalloc byte[1024];
        var defaults = stackalloc byte[1024];
        var allocations = new List<nint>();
        posix_spawn_file_actions_init(actions);
        posix_spawnattr_init(attributes);
        try
        {
            fixed (byte* slave = slavePath)
            {
                // On Linux setsid runs before the file actions, so opening the slave as fd 0 makes it the controlling
                // terminal. On macOS it runs after them; bash reopens the slave (see the remarks).
                Check(posix_spawn_file_actions_addclose(actions, master), "posix_spawn_file_actions_addclose");
                Check(posix_spawn_file_actions_addopen(actions, 0, slave, ORdWr, 0), "posix_spawn_file_actions_addopen");
                Check(posix_spawn_file_actions_adddup2(actions, 0, 1), "posix_spawn_file_actions_adddup2");
                Check(posix_spawn_file_actions_adddup2(actions, 0, 2), "posix_spawn_file_actions_adddup2");
                if (startInfo.WorkingDirectory is not null && s_addChdir != 0)
                {
                    var directory = Utf8(startInfo.WorkingDirectory, allocations);
                    var addChdir = (delegate* unmanaged<void*, byte*, int>)s_addChdir;
                    Check(addChdir(actions, directory), "posix_spawn_file_actions_addchdir_np");
                }

                sigemptyset(mask);
                sigemptyset(defaults);
                foreach (var signal in DefaultSignals)
                {
                    sigaddset(defaults, signal);
                }

                Check(posix_spawnattr_setsigmask(attributes, mask), "posix_spawnattr_setsigmask");
                Check(posix_spawnattr_setsigdefault(attributes, defaults), "posix_spawnattr_setsigdefault");
                Check(
                    posix_spawnattr_setflags(attributes, (short)(PosixSpawnSetSid | PosixSpawnSetSigMask | PosixSpawnSetSigDef)),
                    "posix_spawnattr_setflags");

                var argv = Utf8Array(arguments, allocations);
                var envp = Utf8Array(environment, allocations);
                int processId;
                var result = posix_spawnp(&processId, Utf8(fileName, allocations), actions, attributes, argv, envp);
                if (result != 0)
                {
                    throw new IOException($"Failed to start '{startInfo.FileName}': posix_spawnp returned errno {result}.");
                }

                return processId;
            }
        }
        finally
        {
            posix_spawn_file_actions_destroy(actions);
            posix_spawnattr_destroy(attributes);
            foreach (var allocation in allocations)
            {
                NativeMemory.Free((void*)allocation);
            }
        }
    }

    // Like posix_spawnp: a name with a slash is used as is, otherwise the test process's PATH is searched.
    private static string ResolveProgram(PtyStartInfo startInfo)
    {
        var fileName = startInfo.FileName;
        IEnumerable<string> candidates = fileName.Contains('/')
            ? [fileName]
            : (Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin")
                .Split(':')
                .Select(directory => Path.Combine(directory.Length == 0 ? "." : directory, fileName));
        var allocations = new List<nint>();
        try
        {
            foreach (var candidate in candidates)
            {
                if (access(Utf8(candidate, allocations), XOk) == 0 && !Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        finally
        {
            foreach (var allocation in allocations)
            {
                NativeMemory.Free((void*)allocation);
            }
        }

        throw new IOException($"Failed to start '{fileName}': no executable file found (errno {ENoEnt}).");
    }

    private static void Check(int result, string function)
    {
        if (result != 0)
        {
            throw new IOException($"{function} failed (errno {result}).");
        }
    }

    private static byte* Utf8(string text, List<nint> allocations)
    {
        var length = Encoding.UTF8.GetByteCount(text);
        var pointer = (byte*)NativeMemory.Alloc((nuint)length + 1);
        allocations.Add((nint)pointer);
        Encoding.UTF8.GetBytes(text, new Span<byte>(pointer, length));
        pointer[length] = 0;
        return pointer;
    }

    private static byte** Utf8Array(IReadOnlyList<string> values, List<nint> allocations)
    {
        var pointers = (byte**)NativeMemory.Alloc((nuint)(values.Count + 1), (nuint)sizeof(nint));
        allocations.Add((nint)pointers);
        for (var i = 0; i < values.Count; i++)
        {
            pointers[i] = Utf8(values[i], allocations);
        }

        pointers[values.Count] = null;
        return pointers;
    }

    // glibc 2.29+, musl 1.1.24+ and macOS 10.15+ export it; the main program handle searches the loaded libc.
    private static nint ResolveAddChdir()
        => NativeLibrary.TryGetExport(NativeLibrary.GetMainProgramHandle(), "posix_spawn_file_actions_addchdir_np", out var function)
            ? function
            : 0;

    private void WaitForExit()
    {
        int status;
        while (true)
        {
            if (waitpid(ProcessId, &status, 0) == ProcessId)
            {
                break;
            }

            var error = Marshal.GetLastPInvokeError();
            if (error != EIntr)
            {
                SetExitUnknown(new InvalidOperationException(
                    $"waitpid({ProcessId}) failed with errno {error}; the exit status was collected elsewhere."));
                CloseSlave();
                return;
            }
        }

        // WIFEXITED: exit code; WIFSIGNALED: 128 + signal, as shells report it.
        var signal = status & 0x7f;
        SetExited(signal == 0 ? (status >> 8) & 0xff : 128 + signal);

        // Only now may the master report EIO: the reader drains the rest of the output, then sees the end.
        CloseSlave();
    }

    private void CloseSlave()
    {
        var slave = Interlocked.Exchange(ref _slave, -1);
        if (slave >= 0)
        {
            close(slave);
        }
    }
}
