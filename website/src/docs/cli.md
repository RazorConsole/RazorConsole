# Command-Line Tool

`razorconsole` starts a console app in a pseudoterminal, presses keys and clicks, waits for the screen to reach a state, and reads the app's component tree, focus, route, errors and logs. Every result can be JSON with a fixed set of exit codes, so shell scripts, CI jobs and AI agents can drive an app without a human at the terminal.

```bash
dotnet tool install --global RazorConsole.Cli --prerelease
razorconsole doctor
```

The tool needs .NET 8 or newer. Sessions run in a real pseudoterminal: ConPTY on Windows 10 version 1809 or later, a POSIX PTY on Linux and macOS. `doctor` checks that all of this works on your machine.

### Native AOT

The Native AOT build starts without a .NET runtime of its own (the apps it runs with `--project` or `--dll` still need .NET). One command installs it from the latest [GitHub release](https://github.com/RazorConsole/RazorConsole/releases), after checking its SHA-256 checksum. It picks x64 or arm64 for your machine.

macOS and Linux (installs to `~/.local/share/razorconsole` and links `~/.local/bin/razorconsole`):

```bash
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s -- --app Cli
```

Windows PowerShell (installs to `%LOCALAPPDATA%\RazorConsole\Cli` and adds it to your user `PATH`):

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1))) -App Cli
```

Open a new terminal and run `razorconsole doctor`. Add `--channel nightly` (`-Channel Nightly` on Windows) for the latest nightly build. You can also download `razorconsole-<version>-<platform>-<arch>.zip` (Windows) or `.tar.gz` (Linux and macOS) from the release and unpack it anywhere.

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

`dotnet` is the first one on the `PATH` of the session host (`PATHEXT` on Windows), invoked as it is, so a version manager's shim (mise, asdf) stays in charge of the SDK; only without one does the host try `DOTNET_HOST_PATH`, `DOTNET_ROOT` and the default install location. The build, the framework query and `dotnet run` run in the project's directory, so `global.json` and a directory-based version manager pick the same SDK as `dotnet build` there. That directory is also the app's working directory unless you pass `--cwd`. Output of `dotnet` is read as UTF-8.

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
| `not_rendered_yet` | The app registered its endpoint but has not rendered its first frame. `tree`, `find`, `inspect`, `inspect-at`, `focus` and the element commands wait for it within `--timeout`, and fail with this reason instead of answering with an empty tree. An app that renders nothing after its first frame still gets an empty tree. |
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
| `RAZORCONSOLE_CLI_HOME` | The directory for the host's files (lock, socket, discovery files, and `host.log`, where the host records an unexpected failure that ended it or a client connection; it is capped at 1 MiB and the previous one is kept as `host.log.1`). Default: `%LOCALAPPDATA%\RazorConsole\cli` on Windows, `$TMPDIR/razorconsole` on macOS, `$XDG_RUNTIME_DIR/razorconsole` or `~/.cache/razorconsole/run` on Linux. Use a private one per CI job. |
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
| `errors`, `logs`, `stats` | The errors the app caught, its log entries (`--level`, `--category`), and the app's statistics: the pipeline, the number of frames delivered to observers (frames the renderer built but replaced before delivery are not counted), the latest frame and the uptime. `--follow` streams new entries. |
| `wait`, `assert` | Wait for a condition up to `--timeout` (default 10s), or check it now and exit 1 if it does not hold. `assert --not` inverts it. |
| `doctor`, `host` | Check the setup; manage the background host. |
| `mcp` | Serve these commands to an AI agent as an [MCP server](#mcp-server) over stdio. |

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

In the tree, a `<Markup>` element has a `text` field (`--json`) with its rendered content, the markup removed, like a text node has; its `data-content` attribute stays as it is. In the text output of `tree`, text is shown in quotes on one line with `\n`, `\r`, `\t` and quotes escaped, and whitespace-only text nodes are left out. `--json` keeps them.

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

A failed `assert` also carries a short tree of the app (depth 3) as `error.data.tree`, fetched with a budget of its own (about 2s) no matter how much of `--timeout` is left. The key is always there: when the tree cannot be fetched it is `null` and `error.data.treeReason` says why, either an app layer reason (`disabled`, `not_connected_yet`, `not_rendered_yet`, `incompatible_protocol`) or an error code such as `timeout`.

## MCP server

`razorconsole mcp` serves the same operations to an AI agent as a [Model Context Protocol](https://modelcontextprotocol.io) server over stdio. It uses the same session host, state directory and `RAZORCONSOLE_CLI_HOME` as the other commands, so an agent and a developer see the same sessions: `razorconsole list` shows what the agent started. Standard output carries only protocol messages; logs go to standard error. There is no HTTP transport, no screenshots as images and no recording.

**Claude Code**

```bash
claude mcp add razorconsole -- razorconsole mcp
```

or, for a project, `.mcp.json`:

```json
{
  "mcpServers": {
    "razorconsole": { "command": "razorconsole", "args": ["mcp"] }
  }
}
```

**VS Code** (`.vscode/mcp.json`):

```json
{
  "servers": {
    "razorconsole": { "type": "stdio", "command": "razorconsole", "args": ["mcp"] }
  }
}
```

`-s <name>` and `--timeout <duration>` before `mcp` set the default session and timeout of the tools. Without `-s`, an agent should pass `session` to every call: the current session is shared with the developer, and `start_session` returns the name to use.

### Agent plugin

The [RazorConsole agent plugin](https://github.com/RazorConsole/RazorConsole/tree/main/plugins/razorconsole) installs this MCP server and two skills in one step: `razorconsole` (build apps from the documentation, never from guessed APIs) and `razorconsole-inspect` (run and verify apps with the tools below). It needs the `razorconsole` command on your `PATH`, because the plugin's server is `razorconsole mcp`; install it as shown at the top of this page first.

```bash
# Claude Code
claude plugin marketplace add RazorConsole/RazorConsole
claude plugin install razorconsole@razorconsole

# GitHub Copilot CLI
copilot plugin install RazorConsole/RazorConsole:plugins/razorconsole

# OpenAI Codex (then install razorconsole from the plugins list)
codex plugin marketplace add RazorConsole/RazorConsole

# Google Antigravity (installs from a clone, because the plugin is in a subdirectory)
git clone https://github.com/RazorConsole/RazorConsole
agy plugin install ./RazorConsole/plugins/razorconsole
```

In VS Code, run **Chat: Install Plugin from Source** with `https://github.com/RazorConsole/RazorConsole`. Ollama has no plugin format: run Claude Code or Codex through `ollama launch` with a model that supports tool calling, and install the plugin there. The plugin's README has the details for each client and the `dnx` alternative.

### Tools

Each tool runs the operation of the command with the same name and returns what `--json` prints. Tools that read are marked read-only; tools that send input, navigate, stop or restart are marked destructive, so a client can ask before it runs them. Every tool takes an optional `timeout`, and every duration (`timeout`, and `idle` of `wait_for` and `assert`) is either a string with a unit (`500ms`, `10s`, `2m`) or a number of seconds (`30`, `2.5`). A number is seconds, never milliseconds: `60000` is read as 60,000 seconds and refused as over the limit, and the error says to use `60` or `"60s"`, or a string such as `"500ms"` for milliseconds. `wait_for` and `assert` allow a timeout of at most 5 minutes, the others 10.

A selector is always called `selector`, on `send_keys` (focus that element first), `click`, `wait_for` and `assert` (the `selector` condition), `get_tree`, `find_elements` and `inspect_element`. The command-line flags keep their names (`--element`).

| Group | Tools |
| --- | --- |
| Sessions | `start_session`, `list_sessions`, `session_info`, `stop_session`, `restart_session`, `resize_session`, `attach_app`, `detach_session` |
| Input | `send_keys`, `type_text`, `paste_text`, `send_raw`, `click`, `mouse` (`move`, `down`, `up`, `wheel`) |
| Screen | `screenshot` (`text`, `ansi`, or `json` with styled cells, cursor and modes; optional `region`), `get_frames`, `diff_frames`, `get_output` |
| Wait and assert | `wait_for`, `assert` (one condition: `text`, `regex`, `gone`, `idle`, `exit`, `selector`, `focus` or `route`) |
| App layer | `get_tree`, `find_elements`, `inspect_element`, `inspect_at`, `get_focus`, `set_focus`, `get_route`, `navigate`, `get_errors`, `get_logs`, `get_stats` |
| Environment | `doctor` |

`use`, `host status` and `host stop` have no tool: `use` would change the developer's current session, and stopping the host would end their sessions. `errors --follow` and `logs --follow` are replaced by cursors (below).

### Resources and prompts

| Resource | Content |
| --- | --- |
| `razorconsole://sessions` | The sessions, as `list_sessions` returns them. |
| `razorconsole://session/{name}/screen` | The text on the session's screen now. |
| `razorconsole://session/{name}/tree` | The component tree with layout, as `get_tree` returns it. |

| Prompt | What it asks the agent to do |
| --- | --- |
| `check_keyboard_accessibility` | Walk the focus order with `get_focus` and `send_keys`, and report elements that cannot be reached, show no focus, trap focus or ignore Enter. |
| `check_layout` | Read `get_tree` with layout and `screenshot` at the current size, at 80x24 and at 40x12, and report zero-sized, offscreen and overlapping elements and cut-off text. |

Both take an optional `session`.

### Results and errors

A tool result is one text block with the JSON of the command's `--json` output, `{"ok":true,"session":"s1",…}`. A failed operation is a result with `isError: true` and the same body as the command prints: `{"ok":false,"error":{"code","message","data"}}`, with the codes of the [exit code table](#output-and-exit-codes), such as `element_not_found`, `ambiguous_selector` (with the candidates), `timeout` (with `data.screen`), `process_exited` (with `outputTail` and `lastErrors`) and `host_unavailable`. Arguments that are missing, of the wrong type or refused by the command (two click targets, `delta` on a move) are a `usage` error whose `data.errors` lists every problem. Only a malformed request, an unknown tool, prompt or resource is a JSON-RPC error.

`assert` is the one difference: a condition that does not hold is an answer, not an error. The result is `{"ok":true,"passed":false,"failure":{"code":"assertion_failed","message","data":{"screen",…}}}`. A passing assert has `"passed":true`.

When the client sends `notifications/cancelled`, the running operation is cancelled the way Ctrl+C cancels a command: a wait stops, and the host is told to stop working. The cancelled request gets no answer.

### Size limits

A result that can grow is cut, never silently: it gets a `truncated` array of `{"field","kept","total","hint"}`, and the hint says what to do. A result is at most 100,000 characters of JSON.

| Tool | Limit |
| --- | --- |
| `screenshot` | Rows of `cells` (then `lines`, `ansi`) are cut from the bottom; read a smaller `region`. |
| `get_frames` | At most 50 frames; the oldest are dropped first. |
| `diff_frames` | Later changed lines are dropped. |
| `get_output` | At most `maxBytes` (default 64 KiB, at most 1 MiB). When more may follow, call again with `since` set to `next`. |
| `get_tree`, `razorconsole://session/{name}/tree` | Without `depth` the tree shows 3 levels and is cut to about 20,000 characters: the deepest levels go first, and the nodes that lost children show `childCount`. An explicit `depth` may return up to 100,000 characters. Start with a small `depth` or a `selector`, and use `find_elements` to locate things. |
| `find_elements` | At most 100 matches. |
| `get_logs`, `get_errors` | At most `limit` entries (default 100, at most 1000), oldest first. Pass `next` as `since` for the next page. A log `message` is cut at 2,000 characters, an `exception` or error `details` at 4,000, with `…[+N chars]`. |

## What `errors` can and cannot show

`errors` is the app's own record of exceptions its renderer caught. It is not a crash log.

**It shows** the exceptions that reached the renderer:

- an event handler that throws (a click or key handler),
- a component that throws while it is created or rendered.

A frame that could not be built is not in `errors`: it is reported in `logs --level Error`.

Each entry has a sequence number, a time, a `source` (`render`), the exception type, its message and the full stack trace. The app keeps the last 50. The same exceptions are also in `logs --level Error`, and `logs` (the last 1000 entries, Debug and up, whatever other logging providers the app uses) shows more than `errors` does: failures that are only logged, such as the input loop failing on a key, a focus target that could not be updated, or a component that could not be mounted.

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
