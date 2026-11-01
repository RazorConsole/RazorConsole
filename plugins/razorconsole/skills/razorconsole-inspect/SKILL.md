---
name: razorconsole-inspect
description: "Use this skill whenever running, checking or testing a RazorConsole app: starting it in a pseudoterminal, reading the screen or component tree, pressing keys or clicking, waiting for text, asserting what renders, checking focus or keyboard accessibility, or writing RazorConsole.Testing tests (HeadlessTerminal, PtyTerminal). Trigger on requests like 'check that the app renders the menu', 'does the button work', 'run the app and see', 'verify the layout' or 'write an end-to-end test', even when no tool is named."
---

# RazorConsole Inspect

Use this workflow to run a RazorConsole app and verify what it shows and how it responds, through the `razorconsole` MCP tools, the `razorconsole` command line, or RazorConsole.Testing. The reference for everything below is `website/src/docs/cli.md` (https://razorconsole.com/blog/cli/) and `website/src/docs/testing.md` (https://razorconsole.com/blog/testing/). When this skill and those pages disagree, the pages win.

## Before you start: is the CLI installed?

The MCP server is `razorconsole mcp`, so the `razorconsole` command must exist.

- If the `razorconsole` MCP tools are not available, or `razorconsole --version` fails, **stop and tell the user to install the CLI**. Do not work around it with raw `dotnet run`, screen scraping or guessed output.
- Give the user the install command from the plugin README (`plugins/razorconsole/README.md`, "Requirements"):
  ```bash
  dotnet tool install --global RazorConsole.Cli --prerelease
  razorconsole doctor
  ```
  Native AOT installers for macOS, Linux and Windows are in the same README. After installing, the agent client may need a restart to start the MCP server.
- If `razorconsole doctor` reports a failed check, show the user that output.

## Goal

Drive the real app, look at the real screen, and report what you observed, not what the code should do.

## The MCP loop

1. `start_session` with exactly one of `project` (builds first), `dll` or `exe`. Use the returned name as `session`.
2. `wait_for` the first state you expect, for example `text`.
3. Look: `screenshot` (add `region` to keep it small), `find_elements`, or `get_tree` with a small `depth` (2 or 3) or a `selector`.
4. Act with `send_keys`, `type_text`, `click` and similar tools. Prefer a `selector` over screen coordinates.
5. `wait_for` the result, or `assert` it. A failed `assert` returns `passed: false` with the screen; it is not an error.
6. `stop_session` when done.

Always pass `session` on every call: the current session is shared with the developer.

`wait_for` and `assert` take one condition: `text`, `regex`, `gone`, `idle`, `exit`, `selector`, `focus` or `route` (`assert` also takes `not`). Durations are strings with a unit (`500ms`, `10s`, `2m`) or a number of seconds; they are never milliseconds. The default is 10s, at most 5m for `wait_for` and `assert`, 10m for the other tools.

## Reference (source of truth: `website/src/docs/cli.md`)

Selectors (`selector` in the tools, `--element` on the command line):

| Selector | Matches |
| --- | --- |
| `hook:<name>` | `data-vnode-hook="<name>"`; the most stable |
| `focus:<key>`, `key:<key>`, `id:<id>` | focus key, `@key`, generated id |
| `text:<s>`, `text~:<regex>` | element text in the tree |
| `tag:<name>[attr=value]`, `path:/0/2/1` | tag and attributes, or position |

Errors you will meet:

| Code | Do this |
| --- | --- |
| `app_layer_unavailable` | The tree, focus and element tools need a Development or Debug build with diagnostics on. Use the terminal tools (`screenshot`, `wait_for` with `text`), or run a Debug build. Check the `reason` in the error. |
| `element_not_found`, `ambiguous_selector` | Narrow the selector; use `find_elements` first. A `find_elements` with no match is not an error. |
| `timeout` | Read `data.screen` before retrying; the screen usually shows why. |
| `process_exited` | Read `data.exitCode`, `outputTail` and `lastErrors`. |
| `host_unavailable`, `pty_unsupported` | Run `razorconsole doctor` and show the user the result. |

Clicking needs `EnableMouseEvents` on in the app; without it, press keys instead.

## Without MCP tools, with the command line

Same loop, one command per step: `razorconsole start <name> --project <dir>`, `wait --text`, `tree --depth 2`, `click --element hook:save`, `assert --text`, `screen`, `stop`. Pass `-s <name>` on every command, add `--json` for parseable output, and branch on the exit code. The exit code table is in `cli.md`.

## Tests instead of a live session

For a repeatable check, write a test with `RazorConsole.Testing` (`dotnet add package RazorConsole.Testing`):

- `HeadlessTerminal.StartAsync<TComponent>` for component tests in the test process.
- `PtyTerminal.StartDotnetAsync` for end-to-end tests of a built app in a real pseudoterminal; tag them `[Trait("Category", "E2E")]`.
- Wait for the expected state (`WaitForTextAsync`, `WaitUntilAsync`) instead of sleeping. A `PtyTerminal` input call returns once the bytes are written, so always follow it with a wait.

## Definition of done

- You ran the app and report what the screen showed, with the text or tree you read.
- Every session you started is stopped.
- If you could not run the app, you say why (missing CLI, failed build, unsupported terminal).

## Anti-patterns

- Working around a missing `razorconsole` CLI instead of telling the user to install it.
- Calling tools without `session`, or leaving sessions running.
- Asking for the full tree without `depth` or `selector`.
- Clicking by coordinates when a `hook:` selector exists.
- Retrying a timed-out wait without reading the screen it returned.
