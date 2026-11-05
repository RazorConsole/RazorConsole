# Develop RazorConsole apps with an AI agent

This tutorial walks through one change, start to finish, with a coding agent that has the RazorConsole plugin: the agent reads the documentation, writes the component, builds it, runs the real app in a terminal, looks at the screen, clicks, and checks the result. You watch, review and decide.

The plugin gives the agent two things it does not have on its own: the documentation (so it does not guess component names), and a way to run and see the app (so it does not report a change as working because it compiled).

## What you need

1. The `razorconsole` command-line tool. The plugin's MCP server is `razorconsole mcp`, so the command must be on your `PATH`:

   ```bash
   dotnet tool install --global RazorConsole.Cli --prerelease
   razorconsole doctor
   ```

   `doctor` starts a session and prints a checklist. Fix a failed check before going on; the [Command-Line Tool guide](/blog/cli/) explains each one.

2. The plugin, in an agent client that supports plugins. In Claude Code:

   ```bash
   claude plugin marketplace add RazorConsole/RazorConsole
   claude plugin install razorconsole@razorconsole
   ```

   The [plugin section of the guide](/blog/cli/#agent-plugin) has the commands for GitHub Copilot, OpenAI Codex, Google Antigravity and VS Code. Restart the client afterwards so it starts the MCP server.

3. A RazorConsole app to change. This tutorial uses the counter from [Chapter 2 of the interactive tutorial](/docs/tutorial/state-and-events/), but any app works. Build it in Debug: the tree and element tools need the app's diagnostics endpoint, which only Development and Debug builds register.

To check the setup, ask the agent to list the RazorConsole tools it has. You should see `start_session`, `screenshot`, `get_tree`, `click`, `wait_for`, `assert` and the rest of the [tool list](/blog/cli/#tools).

## The loop

| Step | The agent | Where it comes from |
| --- | --- | --- |
| 1. Read | Fetches `llms.txt` and the page of each component it is about to use | The `razorconsole` skill |
| 2. Write | Changes the `.razor` file, matching the project's style | The `razorconsole` skill |
| 3. Build | Runs `dotnet build` and fixes compile errors | The `razorconsole` skill |
| 4. Run | `start_session` starts the app in a pseudoterminal | `razorconsole-inspect` and the MCP server |
| 5. Look | `screenshot`, `get_tree`, `find_elements` | `razorconsole-inspect` and the MCP server |
| 6. Act | `click`, `send_keys`, `type_text` | `razorconsole-inspect` and the MCP server |
| 7. Check | `wait_for` and `assert` on the result; `stop_session` | `razorconsole-inspect` and the MCP server |

Steps 4 to 7 repeat until the screen shows what you asked for.

## Walkthrough: add a +10 button

The counter has `-1`, `+1` and `Reset` buttons. Ask for one more:

> Add a **+10** button to the counter, next to **+1**. Run the app and check that clicking it adds ten.

### 1. The agent reads before it writes

The `razorconsole` skill makes the agent open `https://razorconsole.com/llms.txt` and the page of `TextButton` before it touches the file. You can see this in the tool calls: documentation fetches come first, the edit second. If the agent writes a component without fetching anything, tell it to use the skill; a component name from memory is the most common source of a build error.

The change itself is small:

```razor
<Columns>
    <TextButton Content="-1" OnClick="() => Change(-1)" />
    <TextButton Content="+1" OnClick="() => Change(1)" />
    <TextButton Content="+10" OnClick="() => Change(10)" />
    <TextButton Content="Reset" OnClick="Reset" />
</Columns>
```

### 2. The agent builds

It runs `dotnet build` on the project. A compile error here means a guessed name or parameter, and the agent goes back to the documentation page rather than trying variations.

### 3. The agent runs the real app

The agent starts the app with `start_session` and `project` set to the app's directory (the tool builds first), and keeps the session name it gets back. Every later call carries that name as `session`, because the current session is shared with you.

It waits for something it expects on the first screen instead of sleeping:

```text
start_session   project: ./Counter
wait_for        session: s1   text: "Count: 0"
screenshot      session: s1
```

`screenshot` returns the text on the screen. This is the first moment the agent knows the app starts at all.

### 4. The agent finds the button and clicks it

A selector names an element. `TextButton` has no `data-vnode-hook` of its own, so the agent selects by text; on an element that does accept the attribute (`Panel`, `Box`, `Flex`, a plain `div`), a hook such as `hook:save` is the most stable choice and the agent adds one when it needs to drive that element.

```text
find_elements   session: s1   selector: "text:+10"
click           session: s1   selector: "text:+10"
wait_for        session: s1   text: "Count: 10"
assert          session: s1   text: "Count: 10"
```

`find_elements` finding nothing is not an error, only an empty list, so the agent uses it to check that the button is on the screen before it clicks. Clicking sends a real mouse click, which needs `options.ConsoleLiveDisplayOptions.EnableMouseEvents = true` in the app. Without it the agent presses keys instead: `send_keys` with `Tab` to move focus and `Enter` to activate.

### 5. A failed check shows its evidence

If something is wrong, `assert` answers `passed: false`; it is not a tool error. The answer carries the screen at that moment, and a short tree of the app:

```text
assert   session: s1   text: "Count: 10"
→ {"ok":true,"passed":false,"failure":{"code":"assertion_failed","data":{"screen":"…","tree":{…}}}}
```

The agent reads the screen and the tree, fixes the component, and runs `restart_session`, which stops the app and starts it again with the same options; for a project it builds again first. Then it repeats from the `wait_for`. A `wait_for` that times out carries the screen in the same way, so the agent reads it before retrying instead of waiting longer.

### 6. The agent cleans up

When the check passes, `stop_session` ends the session. A session left running keeps its app alive in the background until someone stops it.

## Watch it work

The agent and the command line share one session host, so anything the agent starts is visible to you. In a second terminal:

```bash
razorconsole list
razorconsole screen -s s1
razorconsole tree -s s1 --depth 2
```

`list` shows the sessions with their state. `screen` prints what the agent's app shows right now, and `tree` shows the component tree, which is the quickest way to see why a selector did not match. You can also type into the session yourself (`razorconsole keys -s s1 Tab`), but then the agent is no longer the only one changing the screen, so do it between its steps. See [Sessions and the host](/blog/cli/#sessions-and-the-host).

## Keep what you learned: ask for a test

A live check proves the change once. Ask the agent to keep it:

> Write a test for the +10 button.

The `razorconsole-inspect` skill points the agent to `RazorConsole.Testing`: a `HeadlessTerminal` test for the component, or a `PtyTerminal` test for the built app, tagged as end-to-end. Both wait for the expected state instead of sleeping, so the test is not flaky for the same reason a fixed delay would be. The [Testing guide](/blog/testing/) has the full API.

## Prompts worth knowing

Two checks ship as MCP prompts, and you can also ask for them in plain words:

- **Keyboard accessibility** (`check_keyboard_accessibility`): the agent walks the focus order and reports elements that cannot be reached, show no focus, trap focus or ignore <kbd>Enter</kbd>.
- **Layout** (`check_layout`): the agent reads the tree with layout and a screenshot at the current size, at 80x24 and at 40x12, and reports zero-sized, offscreen and overlapping elements and cut-off text.

## When it does not work

| What you see | What it means |
| --- | --- |
| The agent has no RazorConsole tools | The CLI is not on `PATH`, or the client was not restarted after installing the plugin. Run `razorconsole --version` and `razorconsole doctor`. |
| `app_layer_unavailable` on `get_tree` or `click` with a selector | The app is not a Development or Debug build, or has diagnostics turned off. The terminal tools (`screenshot`, `wait_for` with `text`) still work. The `reason` in the error says which. |
| A click changes nothing | The app has mouse events off. Enable `EnableMouseEvents`, or have the agent press keys. |
| `timeout` | The expected text never appeared. The error carries the screen: read it first. |
| `process_exited` | The app crashed. The error has the exit code, the end of the output and the last errors the app reported. |
| `pty_unsupported` | The OS has no pseudoterminal. Windows needs 10 1809 or later. |

## Where to go next

- [Command-Line Tool](/blog/cli/): every command and tool, the selectors, exit codes and size limits.
- [Testing](/blog/testing/): keep the checks as tests.
- [Hot Reload](/blog/hot-reload/): for fast human iteration with `dotnet watch`, next to the agent's loop.
- The [plugin's README](https://github.com/RazorConsole/RazorConsole/tree/main/plugins/razorconsole): per-client install details and how the plugin is laid out.
