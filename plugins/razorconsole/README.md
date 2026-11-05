# RazorConsole agent plugin

One install gives a coding agent two RazorConsole skills and the RazorConsole MCP server.

| Skill | Use |
| --- | --- |
| `razorconsole` | Building RazorConsole apps. The agent reads [llms.txt](https://razorconsole.com/llms.txt) and the page for the component it uses before writing code, and never guesses names or parameters. |
| `razorconsole-inspect` | Running and verifying apps: the MCP loop (`start_session`, `wait_for`, `screenshot`, `get_tree`, actions, `assert`), the command-line equivalents, and `RazorConsole.Testing`. |

The MCP server is `razorconsole mcp`; see the [Command-Line Tool guide](https://razorconsole.com/blog/cli/#mcp-server).

## Requirements

1. **The `razorconsole` CLI on your `PATH`.** The plugin's MCP server runs `razorconsole mcp`. It needs .NET 8 or newer and a pseudoterminal (Windows 10 1809 or later, Linux, macOS).

   ```bash
   dotnet tool install --global RazorConsole.Cli --prerelease
   razorconsole doctor
   ```

   Or install the Native AOT build, which needs no .NET runtime of its own (see the [guide](https://razorconsole.com/blog/cli/#native-aot) for details):

   ```bash
   # macOS and Linux
   curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s -- --app Cli
   ```

   ```powershell
   # Windows PowerShell
   & ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1))) -App Cli
   ```

2. An agent client from the list below.

The app-layer tools (`get_tree`, `find_elements`, clicking an element) need the app to be a Development or Debug build.

## Install

### Claude Code

```bash
claude plugin marketplace add RazorConsole/RazorConsole
claude plugin install razorconsole@razorconsole
```

### VS Code and GitHub Copilot

VS Code (agent plugins are experimental): run **Chat: Install Plugin from Source** and enter `https://github.com/RazorConsole/RazorConsole`, or add the repository as a marketplace in settings:

```json
"chat.plugins.marketplaces": ["RazorConsole/RazorConsole"]
```

Copilot CLI:

```bash
copilot plugin install RazorConsole/RazorConsole:plugins/razorconsole
```

### OpenAI Codex

```bash
codex plugin marketplace add RazorConsole/RazorConsole
```

Then install `razorconsole` from the plugins list in Codex.

### Google Antigravity (`agy`)

`agy plugin install <github-url>` installs a repository root only, and this plugin lives in a subdirectory, so install it from a clone:

```bash
git clone https://github.com/RazorConsole/RazorConsole
agy plugin install ./RazorConsole/plugins/razorconsole
```

If you already installed the plugin in Claude Code, `agy plugin import claude` imports it instead. Antigravity does not load Gemini CLI extensions directly; `agy plugin import gemini` converts them.

### Ollama

Ollama has no plugin format. Run Claude Code or Codex through `ollama launch` with a model that supports tool calling, and install the plugin in that client as above.

## Use the dnx form of the MCP server (optional)

The plugin starts the server with `razorconsole mcp`. If you prefer not to install the tool, change the server definition in your own client config to:

```json
{ "command": "dnx", "args": ["RazorConsole.Cli", "--prerelease", "--yes", "--", "mcp"] }
```

`--yes` needs the .NET 11 SDK; without it `dnx` can stop and ask for confirmation, which hangs a stdio server. The package must also be published on NuGet. The plugin keeps `razorconsole mcp` as the default because it also works with .NET 8 to 10 and with the Native AOT build.

## Layout

| Path | Read by |
| --- | --- |
| `.claude-plugin/plugin.json`, `.mcp.json` | Claude Code |
| `plugin.json`, `mcp.json` | VS Code, GitHub Copilot, Codex (Agent Plugins 1.0) |
| `.antigravity-plugin/plugin.json`, `mcp_config.json` | Google Antigravity |
| `skills/<name>/SKILL.md` | all of them |

The marketplace entries are `.claude-plugin/marketplace.json` (Claude Code, VS Code, Copilot) and `.agents/plugins/marketplace.json` (Codex) at the repository root. `scripts/verify-agent-plugin.cs` fails CI when the files disagree.

## Changing the plugin

Any change to the skills or the MCP config **bumps `version` in every manifest** (`plugin.json`, `.claude-plugin/plugin.json`, `.antigravity-plugin/plugin.json`). Claude Code and Codex reinstall a plugin only when its version changes, so an unbumped change never reaches users. `scripts/verify-agent-plugin.cs` checks that the versions are equal. Run it before committing:

```bash
dotnet run scripts/verify-agent-plugin.cs
```

The plugin version is its own; it is not the `RazorConsole.Cli` package version.
