# LLMAgentTUI

A Codex CLI-inspired agent interface built from real RazorConsole components.
The same `App` is used with a live provider and with a scripted session.
Only session responses, tool results, usage data and approvals are simulated.
The layout and styling follow the supplied Codex 0.157.0 reference, but the
application brand is LLMAgentTUI and the version comes from its own assembly.
Model and directory labels come from session data, not the reference screenshot.
The scripted model label is fixture data, not a model connection.

## Run

```bash
dotnet run --project examples/LLMAgentTUI --framework net10.0 -- --mock
```

No API key, Ollama server or network connection is needed in scripted mode.
Type a prompt and press Enter. F6 advances one scripted event. When the approval
prompt appears, enter 1 to approve or 2 to decline. No command is executed.

Without `--mock`, a real `ChatClientAgent` uses the `Agent` section in
`appsettings.json`. The default is DeepSeek (`deepseek-flash`) with an intentionally
invalid `dummy` API key. Replace the key via User Secrets or an environment
variable before making real requests; no credential is committed.

```bash
dotnet run --project examples/LLMAgentTUI --framework net10.0
```

The example targets net8.0, net9.0, net10.0 and net11.0. Building this checkout
requires the .NET 11 RC1 SDK pinned in the repository `global.json`; .NET 11 support
currently targets a prerelease runtime. Older target frameworks remain supported.

```bash
dotnet run --project examples/LLMAgentTUI --framework net11.0 -- --mock
```

## Configuration

Configuration is loaded beside the built application, so running from the
repository root still finds the correct JSON. The coding workspace remains
the current working directory, not the binary directory.

Override order (highest first): command-line arguments, environment variables,
`appsettings.local.json`, User Secrets in Development, environment-specific JSON,
`appsettings.json`.
The launch profile enables Development for `dotnet run`; direct DLL execution
needs `DOTNET_ENVIRONMENT=Development` to load User Secrets.

For workspace-local configuration, edit
`examples/LLMAgentTUI/appsettings.local.json`:

```json
{
  "Agent": {
    "ApiKey": "YOUR_DEEPSEEK_KEY"
  }
}
```

This optional file is ignored by Git, copied to the build output for `dotnet run`,
and excluded from publish output. It is loaded in every environment, not only
Development. Restart/rebuild after changing it (do not use `--no-build` for that
run). It remains plaintext; do not share the local file or build artifacts.
The local `ApiKey` overrides User Secrets, so remove that property if you prefer
User Secrets instead. New clones can create this file using the example above.

Alternatively, store a real API key outside the repository:

```bash
dotnet user-secrets set "Agent:ApiKey" "YOUR_DEEPSEEK_KEY" --project examples/LLMAgentTUI
```

Or supply `Agent__ApiKey` via the environment. Other configuration keys are
`Agent__Provider`, `Agent__Endpoint`, and `Agent__Model`. All providers currently
use the OpenAI-compatible Chat Completions protocol. For Ollama, set endpoint
`http://localhost:11434/v1`, the installed model name, and a nonempty dummy key.
The old `OPENAI_API_KEY` / `LLMAGENT_*` variables are no longer read. `.env` is
not automatically loaded. Do not put a real key in tracked JSON or launch settings;
User Secrets are outside the repository but are not encrypted.

## Interaction

Typing `/` opens an unboxed two-column command picker. Type to filter, use
Up/Down to select, Tab to complete, Enter or a mouse click to execute, and Esc
to dismiss without changing the draft. Hover and mouse wheel change selection.
Only implemented local commands are listed; the menu is not a list of placeholder
Codex features.

Markdown pipe tables use aligned, width-aware columns, gold headers and horizontal
rules. Cells wrap by terminal display width (including CJK text) and preserve inline
styles and selection. Extremely narrow tables fall back to labeled rows. Lists
preserve ordered starts, nesting, and hanging indentation on wrapped items.

Permission modes are **Ask for approval** (every tool), **Workspace files**
(auto-approve the guarded read/write/edit tools, ask for shell commands), and
**Full access** (auto-approve all registered coding tools). Unknown tools still
require approval. This is an approval policy, **not an OS sandbox**: shell
commands run with the process's permissions, including outside-workspace and
network access. Direct file-tool workspace guardrails remain in every mode.
The default is Ask for approval on each new session.

For DeepSeek, the client adapter preserves `reasoning_content` in assistant
history and reconnects native approval-split tool calls with their reasoning.
It does not disable thinking or expose model reasoning in the transcript.
Regression tests exercise the real SDK and agent against an offline HTTP handler,
including approval/denial, responses without visible text, and the next turn.
See [DeepSeek thinking/tool-call requirements](https://api-docs.deepseek.com/guides/thinking_mode/).

- Enter submits; Shift/Alt+Enter inserts a newline when the terminal reports it.
- Mouse click positions the draft cursor; drag selects text.
- Double/triple click selects a word/logical line; typing replaces the selection.
- Wheel scrolls the transcript. Dragging beyond a text viewport scrolls it.
- Right click copies selected text; transcript links can be clicked or Ctrl-clicked.
- Click a tool heading or use F7 to expand its output.
- Click Back to bottom to return to the latest transcript rows.
- `/usage` opens usage reports, with clickable tabs/controls and a scrollable body.
- `/permission` opens session-local tool permissions (alias: `/permissions`).
  Use arrows or 1–3 to select, Enter to apply, or click a row then Enter.
  Esc closes without changing the policy. Full access requires a second Enter.
  Permissions cannot change during a running request or pending approval.
- Click notifications to inspect session notices; Esc returns to the conversation.
- Tab changes focus; PageUp/PageDown scroll; Ctrl+C exits.

Copy and URL opening use Core's terminal actions. Clipboard support depends on
the terminal accepting OSC 52. Automated tests record these external effects so
they do not overwrite the developer's clipboard or launch a browser.

## Architecture

- `Components/App.razor`: shared application UI.
- `Components/UsageView.razor`: usage UI, populated from session data.
- `Services/AgentSession.cs`: UI state contract (not the framework's session).
- `Services/ChatClientAgentController.cs`: native framework session, streaming,
  tool calls/results, approval responses, cancellation and token usage.
- `Services/CodingTools.cs`: approval-required read/write/edit and bash or
  PowerShell tools; tools belong to the app, not the rendering framework.
- `Services/ScriptedAgentSession.cs`: deterministic offline session.
- `Services/AgentViewState.cs`: real draft, transcript selection and disclosure state.
- Core `TextArea`, `TextSelectionState`, mouse routing and terminal actions:
  production interaction behavior shared with other apps.

Framework behavior is not mocked. The TestTerminal mounts this application's
real root component and sends input through the production event managers.
Live execution no longer uses IChatService. The framework manages conversation
history and tool invocation. See [AGENT-TOOLS.md](AGENT-TOOLS.md) for the pinned
pi source research, implemented subset, package versions and safety boundaries.
Approve individual tools with 1/2 or the approval buttons. Scroll their arguments
before approving. Escape cancels; shell commands are **not sandboxed**.

## Tests and diagnostics

```bash
dotnet test src/RazorConsole.Tests/RazorConsole.Tests.csproj --framework net10.0
```

Integration coverage includes fixed viewport layouts, resizing, selection/editing,
scrolling, copying, links, usage controls and a complete scripted approval flow.
Existing fast unit tests remain.

The empty-session reference test compares every text row at 87 columns by 35
rows and checks header typography, footer colors, composer coordinates and all
composer background cells. The light colors follow the supplied screenshot.
This is cell/style regression coverage, not a completed pixel-diff acceptance:
the exact terminal, font, font size, display scale, palette and cursor rendering
must also be matched before comparing screenshots. Other conversation states
still require their own reference captures.

TestTerminal exposes immutable cell/style snapshots, frame history, focus and
named widget bounds. `DumpDiagnostics()` also includes the last mouse dispatch.
The scripted session exposes an event log; tests control advancement explicitly.

On macOS/Linux, mouse-enabled applications read the terminal byte stream before
key decoding; SGR mouse reports never pass through `Console.ReadKey`. Non-mouse
applications retain the existing console input path. The decoder preserves partial
packets across reads and separates keyboard and mouse events in arrival order.

Optional native Unix input regression (no tmux, no model network access):

```bash
dotnet build examples/LLMAgentTUI -c Release -f net10.0
uv run --with pyte python tests/e2e/terminal-input-pty.py
```

This launches the real application in a PTY with `--mock`, sends 201 wheel reports
(including delayed fragments), checks draft preservation, exercises arrow/Tab menu
selection, and checks that Ctrl+C disables mouse reporting. Pass `--dotnet PATH`
if the SDK is not on PATH. Linux still needs native validation; current native
execution evidence is macOS, in addition to the cross-target .NET unit tests.

Use `--framework net11.0` with the PTY script to exercise the .NET 11 build
(build the example with `-c Release -f net11.0` first).

The reference version and the full acceptance inventory are in
[CODEX-PARITY.md](CODEX-PARITY.md). The inventory distinguishes implemented
headless behavior from native terminal validation; it is not a claim of complete
cross-platform parity. The older DESIGN.md and SCREENSHOT.md describe the
previous chatbot design and are not the visual reference for this interface.
