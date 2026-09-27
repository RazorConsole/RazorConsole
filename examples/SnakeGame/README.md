# Razor Snake

A complete Snake game built with Razor components and RazorConsole. The compact branded terminal UI uses `Panel`, `Rows`, `Columns`, `Flex`, `Markup`, and `SpectreCanvas`, while the game loop, keyboard input, and mouse controls stay inside Razor components.

The project is configured for Native AOT by default, producing a self-contained native executable with no JIT dependency.

The UI requires a terminal of at least **80×24**. Above that minimum, the game grid expands with the terminal. Smaller terminals show a resize prompt instead of clipping the controls.

## Play

```bash
dotnet run --project examples/SnakeGame/SnakeGame.csproj -f net10.0
```

The game always uses RazorConsole's native WidgetLayout pipeline. Rendering-pipeline flags and environment variables are intentionally ignored.

## Controls

- Arrow keys or `WASD`: steer
- `Space`: start, pause, or resume
- `R`: restart
- `+` / `-`: adjust speed continuously in 1 ms increments
- `Q` or `Escape`: quit
- Mouse: click the direction, pause, reboot, and quit controls; click or drag the continuous speed slider, or wheel it in 1 ms increments

The in-app footer also contains a terminal hyperlink to the [RazorConsole website](https://razorconsole.github.io/RazorConsole/).

## Publish a Native AOT binary

Native AOT publishes for one operating system and architecture at a time. Choose the RID for the target machine:

```bash
# macOS Apple Silicon
dotnet publish examples/SnakeGame/SnakeGame.csproj -c Release -f net10.0 -r osx-arm64

# Linux x64
dotnet publish examples/SnakeGame/SnakeGame.csproj -c Release -f net10.0 -r linux-x64

# Windows x64 (run on Windows)
dotnet publish examples/SnakeGame/SnakeGame.csproj -c Release -f net10.0 -r win-x64
```

The native executable is written beneath `artifacts/publish/SnakeGame/release_<framework>_<rid>/` because this repository uses the artifacts output layout.

Native AOT requires the platform toolchain described in the [.NET Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).

## Install a released binary

Stable release on macOS or Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-snake.sh | sh
```

Latest `main` prerelease:

```bash
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-snake.sh | sh -s -- --channel nightly
```

Stable release on Windows PowerShell:

```powershell
irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-snake.ps1 | iex
```

Latest `main` prerelease on Windows PowerShell:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-snake.ps1))) -Channel Nightly
```

The installers detect the operating system and architecture, download the matching Native AOT archive, and verify it against the release SHA-256 manifest. Archives for Linux, Windows, and macOS on x64 and Arm64 are also available from [GitHub Releases](https://github.com/RazorConsole/RazorConsole/releases).
