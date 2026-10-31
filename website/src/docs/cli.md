# Command-Line Tool

`razorconsole` starts a console app in a pseudoterminal, presses keys and clicks, waits for the screen to reach a state, and reads the app's component tree, focus, route, errors and logs. Every result can be JSON with a fixed set of exit codes, so shell scripts, CI jobs and AI agents can drive an app without a human at the terminal.

```bash
dotnet tool install --global RazorConsole.Cli --prerelease
razorconsole doctor
```

The tool needs .NET 8 or newer. Sessions run in a real pseudoterminal: ConPTY on Windows 10 version 1809 or later, a POSIX PTY on Linux and macOS. `doctor` checks that all of this works on your machine.

Each [GitHub release](https://github.com/RazorConsole/RazorConsole/releases) also has a Native AOT build of the tool as `razorconsole-<version>-<platform>.zip` (Windows) or `.tar.gz` (Linux and macOS), for x64 and arm64. Unpack it anywhere and run `razorconsole`; it starts without a .NET runtime of its own, though the apps it runs (`--project`, `--dll`) still need .NET.

## The loop

```bash
razorconsole start demo --project ./MyApp          # build, run, wait for the first frame
razorconsole wait --text "Welcome"                 # wait for the screen
razorconsole tree --depth 2                        # the component tree
razorconsole click --element hook:save             # a real mouse click on that element
razorconsole assert --text "clicked: 1"            # exit 1 if it does not hold
razorconsole screen                                # what the terminal shows
razorconsole stop                                  # Ctrl+C, then kill
```

`start` makes the new session the current one, so the later commands need no session name. Pass `-s <name>` (or set `RAZORCONSOLE_SESSION`) to address another one; scripts that run in parallel should always pass it.

Start the app with `--project <dir>` (builds it first), `--dll <path>` (runs it with `dotnet`) or `--exe <path>`. Everything after `--` goes to the app. The terminal is 120x40 unless you pass `--width` and `--height`, and `--env NAME=value` sets an environment variable for the app.

A project that targets several frameworks (net8.0 to net11.0, like the Gallery) needs one to run. `--framework <tfm>` picks it, for example `--project ./MyApp --framework net9.0`; it is passed to both `dotnet build` and `dotnet run`, and it is a usage error with `--dll` or `--exe`. Without it the tool takes the highest target framework that has an installed .NET runtime, and says which one: `start` prints it (`started demo (pid 26444, 120x40, running, net10.0, …)`) and `--json` has it as `framework`. If none of the project's frameworks can run, `start` fails with exit code 2 and lists the project's frameworks and the installed runtimes. `dotnet run` would otherwise show an interactive "select the target framework" prompt inside the terminal, so the tool always passes the framework explicitly. Only the selected framework is built.

`--configuration <name>` picks the build configuration (Debug when omitted), for example `--project ./MyApp --configuration Release` to reuse an existing Release build. It is passed to both `dotnet build` and `dotnet run`, and it is a usage error with `--dll` or `--exe`. `start` reports it next to the framework (`…, running, net10.0, Release, …`) and `--json` has it as `configuration`. The build runs in the session host, so set build-related environment variables where the host starts, not on the `start` command.

Text output is UTF-8, whatever the console's code page is, so the box corners, `●` and arrows of `screen` come through when you pipe the output to another program. `--json` output is ASCII-escaped and does not depend on it.

```text
$ razorconsole start demo --dll ./bin/Debug/net10.0/MyApp.dll
started demo (pid 26444, 120x40, running, app layer not_connected_yet) dotnet ./bin/Debug/net10.0/MyApp.dll
$ razorconsole wait --text "App layer view"
the screen to contain "App layer view": met after 3 ms
$ razorconsole find hook:save
div#2e84994b6a9e496c99af8b6cf8ec50fb focus=save @0,3 6x1
$ razorconsole click --element hook:save
clicked (2,3)
$ razorconsole assert --text "clicked: 1"
assertion passed: the screen to contain "clicked: 1"
```

## Two layers

The tool works at two levels, and says which one a command needs:

| Layer | What it gives | Works with |
| --- | --- | --- |
| Terminal | `start`, `stop`, `restart`, `resize`, `keys`, `type`, `paste`, `raw`, `mouse`, `screen`, `frames`, `diff`, `output`, `wait --text`, `--regex`, `--gone`, `--idle`, `--exit` | Any program that runs in a terminal |
| App | `tree`, `find`, `inspect`, `inspect-at`, `focus`, `route`, `errors`, `logs`, `stats`, `click --element`, `keys --element`, `wait --element`, `--focus`, `--route`, and the matching `assert` conditions | A RazorConsole app with its diagnostics endpoint on |

If the app layer is not there, an app command fails with exit 4 and `app_layer_unavailable`; the terminal commands keep working. The `reason` in the error data says why:

| `reason` | Meaning |
| --- | --- |
| `not_connected_yet` | The app has not registered its endpoint yet. Commands wait for it within `--timeout` until the app has drawn its first frame and two more seconds have passed. |
| `disabled` | The app drew a frame and registered nothing. It is not a RazorConsole app, it is a Release or Production build, or diagnostics are turned off. |
| `incompatible_protocol` | The app speaks another version of the diagnostics protocol than this tool. |
| `attached` | A terminal command on an attached session, which has no terminal to use (see [Attaching](#attaching-to-a-running-app)). |

### When the endpoint exists

A RazorConsole app registers its diagnostics endpoint when its host environment is `Development` **or** its entry assembly is a Debug build. Release and Production builds never register it, and there is no switch to force it on.

To turn it off in a Development or Debug app:

```csharp
builder.UseRazorConsole<App>(configure: config =>
{
    config.Services.Configure<ConsoleAppOptions>(options =>
        options.Diagnostics = RazorConsoleDiagnostics.Disabled);
});
```

or set `RAZORCONSOLE_DIAGNOSTICS=0` (`false`, `off` and `disabled` also work). The endpoint listens on a named pipe (Windows) or a Unix domain socket that only your user can open, serves at most four connections at a time, and writes a small discovery file so the tool can find the app.

Clicking needs the app to turn on mouse events (`options.ConsoleLiveDisplayOptions.EnableMouseEvents = true`). Without it, press keys instead.

## Sessions and the host

The first command that needs one starts a background **session host**: the same `razorconsole` executable, started as a hidden `__host` verb. The host owns the pseudoterminals, so a session outlives the command that started it. It exits by itself ten minutes after the last session and client are gone. `razorconsole host status` shows it; `razorconsole host stop` stops it and every session in it, which ends the apps it started.

| | |
| --- | --- |
| `RAZORCONSOLE_CLI_HOME` | The directory for the host's files (lock, socket, discovery files). Default: `%LOCALAPPDATA%\RazorConsole\cli` on Windows, `$TMPDIR/razorconsole` on macOS, `$XDG_RUNTIME_DIR/razorconsole` or `~/.cache/razorconsole/run` on Linux. Use a private one per CI job. |
| `RAZORCONSOLE_SESSION` | The session to use when `-s` is not given. |

`list` shows the sessions, `use <name>` changes the current one, `restart` stops a session and starts it again with the same options, and `resize <cols> <rows>` resizes its terminal.

### Attaching to a running app

`razorconsole attach <pid>` connects to a RazorConsole app that was started some other way (a debugger, `dotnet run` in another terminal) through its diagnostics endpoint. An attached session has the app layer only:

- `screen` shows the renderer's canvas (`"source":"renderer"` in JSON), not a terminal screen, and not what the app wrote to the console itself.
- `keys`, `click` and the other input commands go through the endpoint's input queue.
- `restart`, `resize`, `output`, `frames` and `diff` need a terminal, so they fail with `terminal_layer_unavailable` and the reason `attached`.
- `detach` drops the session and the app keeps running. `host stop` never ends an attached app.

## Commands

| Command | What it does |
| --- | --- |
| `start [name]`, `stop`, `restart`, `list`, `use`, `resize` | Manage PTY sessions. |
| `attach <pid>`, `detach` | Manage attached sessions. |
| `keys`, `type`, `paste`, `raw` | Send keys (`Tab`, `Enter`, `Ctrl+C`, `Shift+Tab`, `F5`, `a`), text, a paste, or raw terminal input. `keys --element <selector>` focuses that element first. |
| `click`, `mouse` | Click a cell (`click 10 4`), text (`--text`, `--nth`) or an element (`--element`); send one mouse event. Clicks are real mouse input. |
| `screen`, `frames`, `diff`, `output` | Read the terminal: `--format text\|ansi\|json`, `--region x,y,w,h`; recent frames and what changed; the raw output with byte offsets for `--since`. |
| `tree`, `find`, `inspect`, `inspect-at` | Read the component tree, find elements, inspect one, or see what is at a screen cell. |
| `focus`, `route` | Show or change focus and the current route. `focus --set\|--next\|--prev` and `route --go` change the app's state directly; to press the key, use `keys Tab`. |
| `errors`, `logs`, `stats` | The errors the app caught, its log entries (`--level`, `--category`), and render statistics. `--follow` streams new entries. |
| `wait`, `assert` | Wait for a condition up to `--timeout` (default 10s), or check it now and exit 1 if it does not hold. `assert --not` inverts it. |
| `doctor`, `host` | Check the setup; manage the background host. |

`wait` and `assert` take one condition: `--text`, `--regex` (a .NET regex matched against each line), `--gone`, `--idle <duration>`, `--exit`, `--element <selector>`, `--focus <key>` or `--route <uri>`.

### Selectors

`tree`, `find`, `inspect` and `--element` take `<kind>:<value>`:

| Selector | Matches |
| --- | --- |
| `hook:<name>` | The element with `data-vnode-hook="<name>"`. The most stable choice: add the attribute to what you want to drive. |
| `focus:<key>` | The element with this focus key. |
| `key:<key>`, `id:<id>` | The element with this `@key`, or this generated id. |
| `text:<substring>`, `text~:<regex>` | The element that holds this text, including a `<Markup>` element with this content. |
| `tag:<name>[attr=value]` | An element by tag and attributes. |
| `path:/0/2/1` | A node by position, text nodes included. |

`find` returns every match, or the first with `--first`. A `find` that matches nothing is not an error: it prints `no matches` (`"matches":[]` with `--json`) and exits 0. Commands that need exactly one element (`inspect`, `tree <selector>`, `click --element`, `keys --element`, `focus --set`) exit 3 with `element_not_found` when nothing matches, and with `ambiguous_selector` (listing the candidates) when several do.

Text selectors match the text in the component tree, which includes the content of `<Markup>` elements, with the markup removed. They do not see text that a component draws itself; `click --text` and `wait --text` match the screen.

In the text output of `tree`, text is shown in quotes on one line with `\n`, `\r`, `\t` and quotes escaped, and whitespace-only text nodes are left out. `--json` keeps them.

## Output and exit codes

Without `--json` the result is text on stdout and errors go to stderr as `razorconsole: <message> [<code>]`. With `--json` every command prints exactly one JSON object on stdout (`--follow` prints one per line):

```json
{"ok":true,"session":"demo","errors":[],"next":1}
{"ok":false,"error":{"code":"process_exited","message":"…","data":{"exitCode":3}}}
```

Each error code maps to one exit code, so scripts can branch without parsing:

| Exit | Meaning | Error codes |
| --- | --- | --- |
| 0 | Success | |
| 1 | `assert` failed, or `doctor` found a failed check | `assertion_failed`, `checks_failed` |
| 2 | Usage error | `usage`, `invalid_params` |
| 3 | Not found | `session_not_found`, `element_not_found`, `ambiguous_selector`, `element_not_visible`, `element_not_focusable` |
| 4 | The app layer or terminal layer is not available | `app_layer_unavailable`, `input_unavailable`, `screen_unavailable`, `terminal_layer_unavailable` |
| 5 | A wait timed out | `timeout` |
| 6 | The app's process has exited | `process_exited` |
| 7 | No pseudoterminal on this OS | `pty_unsupported` |
| 8 | The session host could not start, or is another version | `host_unavailable`, `host_version_mismatch` |
| 10 | Internal error | `internal` |
| 130 | Cancelled by the user (Ctrl+C) | `cancelled` |

A failed `assert` and a timed-out `wait` carry the screen: in text after a `--- screen ---` line, in JSON as `error.data.screen`.

## What `errors` can and cannot show

`errors` is the app's own record of exceptions its renderer caught. It is not a crash log.

**It shows** the exceptions that reached the renderer:

- an event handler that throws (a click or key handler),
- a component that throws while it is created or rendered,
- a render snapshot that could not be built.

Each entry has a sequence number, a time, a `source` (`render` or `snapshot`), the exception type, its message and the full stack trace. The app keeps the last 50. The same exceptions are also in `logs --level Error`, and `logs` (the last 1000 entries, Debug and up, whatever other logging providers the app uses) shows more than `errors` does: failures that are only logged, such as the input loop failing on a key, a focus target that could not be updated, or a component that could not be mounted.

**It does not show:**

- exceptions your own code catches,
- failures that are only logged, not rethrown to the renderer (read them with `logs`),
- anything the app prints to the terminal,
- anything thrown outside the renderer, such as on a background thread or in `Main`.

**A crash takes the endpoint with it.** An unhandled exception, `Environment.Exit` or a killed process ends the app, and its diagnostics endpoint goes with it. After that `errors`, `logs`, `tree` and every other app command have nothing to ask. If the app dies during startup, before it registered the endpoint, `errors` never had anything to show.

### When the app exits: `process_exited`

Once the app's process is gone, every command that needs the app or its input fails with exit 6 and `process_exited`. The first one after the exit does too; it does not fall back to `app_layer_unavailable`. To find out why the app died, read the error:

```text
$ razorconsole tree
razorconsole: The process of 'demo' has exited with exit code 3. [process_exited]
--- last errors ---
[1] 2026-10-30T22:05:42.3046102+00:00 render System.InvalidOperationException: diag failure
--- output tail ---
App layer view
…
[Quit]
```

In JSON the same is in `error.data`:

| Field | Content |
| --- | --- |
| `exitCode` | The exit code. Only for sessions the tool started; an attached session has none. |
| `outputTail` | The end of the terminal output as plain text, with escape sequences removed: at most 40 lines and 4 KB. Where .NET prints an unhandled exception, so for a crash it is usually the reason. Only for sessions the tool started, and only when there was any output. |
| `lastErrors` | Up to five of the errors the app reported before it went away, oldest first, in the shape of `errors` entries. For sessions the tool started and for attached ones. |

Two limits to know about:

- The host reads an app's errors about once a second. An error thrown in the last second before the process died can be missing from `lastErrors`, and an app that never registered an endpoint has none.
- `outputTail` is the end of what the terminal received. For a full-screen app that quit normally, that is its last screen, as in the example above. The full output is still there: `screen` and `output` keep working on a PTY session after its process has exited, until you `stop` it.

A crash of an app that never drew anything is the typical case for `outputTail` (the exit code is whatever the platform reports for an unhandled exception; this one is from Windows):

```text
$ razorconsole start demo --dll ./MyApp.dll --no-wait
$ razorconsole wait --exit
the process to exit: met after 1 ms
$ razorconsole keys Tab
razorconsole: The process of 'demo' has exited with exit code -532462766. [process_exited]
--- output tail ---
Unhandled exception. System.InvalidOperationException: diag crash
   at Program.<<Main>$>g__Crash|0_2() in Program.cs:line 78
   at Program.<Main>$(String[] args) in Program.cs:line 32
   at Program.<Main>(String[] args)
```

## `doctor`

`razorconsole doctor` runs eight checks and exits 1 if one fails. It starts the session host if none runs, and stops it again at the end; a host that was already running is left alone.

| Check | What it looks at |
| --- | --- |
| `os.pty` | A pseudoterminal can be created (ConPTY, or `/dev/ptmx`). |
| `os.shell` | On macOS, `/bin/bash` and `/usr/bin/env` exist. |
| `dotnet` | The `dotnet` host is found and lists a runtime. |
| `state.dir` | The state directory is writable and, on Unix, mode 0700. |
| `host.start` | The session host starts (or is already running) and answers. |
| `host.socket` | Its socket or pipe can be opened only by your user. |
| `pty.smoke` | A 20x5 session runs `dotnet --version` and shows its output. |
| `apps.stale` | No discovery files of apps that are gone. `--fix` removes them. |

## Notes

- On Windows, ConPTY re-renders what the app wrote, so `output` can differ from the exact bytes the app emitted.
- On Unix the host's socket path has a length limit (104 bytes on macOS). `doctor` reports a `RAZORCONSOLE_CLI_HOME` or `TMPDIR` that is too long.
- The host and the app endpoints are open to your user only. Any process that runs as you can use them, so treat a session like a terminal you left open.
