# Razor Snake

A complete Snake game built with Razor components and RazorConsole. The compact branded terminal UI uses `Panel`, `Rows`, `Columns`, `Flex`, `Markup`, and `SpectreCanvas`, while the game loop, keyboard input, and mouse controls stay inside Razor components.

The project is configured for Native AOT by default, producing a self-contained native executable with no JIT dependency.

The UI requires a terminal of at least **80×24**. Above that minimum, the game grid expands with the terminal. Smaller terminals show a resize prompt instead of clipping the controls.

## Play

```bash
dotnet run --project gallery/RazorConsole.Snake/RazorConsole.Snake.csproj -f net10.0
```

The game always uses RazorConsole's native WidgetLayout pipeline. Rendering-pipeline flags and environment variables are intentionally ignored.

The game enables synchronized terminal updates (DEC private mode 2026), so a
supporting terminal presents all rows of each frame together. This prevents a
wide snake from appearing to move one row at a time. In terminals without this
support, row-by-row tearing can still occur even though every row uses the same
game tick.

## Controls

- Arrow keys or `WASD`: steer
- `Space`: start, pause, or resume
- `R`: restart
- `+` / `-`: adjust speed continuously in 1 ms increments
- `Q` or `Escape`: quit
- Mouse: click the direction, pause, reboot, and quit controls; click or drag the continuous speed slider, or wheel it in 1 ms increments

The in-app footer also contains a terminal hyperlink to the [RazorConsole website](https://razorconsole.com/).

## Food and width traps

Red food adds one segment and 10 points. Each meal also spawns an amber width trap
on a free cell, separate from the next food. Uneaten traps never expire: they
accumulate as you collect food, making the board progressively harder to navigate.

Touching an amber trap with the head consumes it and permanently widens every
segment by two cells (width 1, 3, 5, ...), without adding length or points.
Each segment occupies a square footprint; the display, food placement, pickups,
wall collisions, and self-collisions all use that footprint. Movement is checked
as a sweep from tail to head: every newly entered cell must be clear of the
existing body, with no exemption based on segment distance. Crossing the
centerline or widening into a wall or body also ends the game.
The occupied cells are drawn once as a continuous body, with only the leading
edge highlighted as the head, so wide horizontal and vertical motion do not
produce overlapping colored blocks.

The sidebar shows the current width and remaining traps. Restart clears all traps
and resets the width. Resizing preserves the game unless a body footprint or trap
would fall outside the new board, in which case it restarts. If no free cell remains
for food, you win; if only one free cell remains after a meal, it is reserved for
food rather than a new trap.

## Publish a Native AOT binary

Native AOT publishes for one operating system and architecture at a time. Choose the RID for the target machine:

```bash
# macOS Apple Silicon
dotnet publish gallery/RazorConsole.Snake/RazorConsole.Snake.csproj -c Release -f net10.0 -r osx-arm64

# Linux x64
dotnet publish gallery/RazorConsole.Snake/RazorConsole.Snake.csproj -c Release -f net10.0 -r linux-x64

# Windows x64 (run on Windows)
dotnet publish gallery/RazorConsole.Snake/RazorConsole.Snake.csproj -c Release -f net10.0 -r win-x64
```

The native executable is written beneath `artifacts/publish/RazorConsole.Snake/release_<framework>_<rid>/` because this repository uses the artifacts output layout.

Native AOT requires the platform toolchain described in the [.NET Native AOT prerequisites](https://learn.microsoft.com/dotnet/core/deploying/native-aot/).

## Install a released binary

Stable release on macOS or Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s -- --app Snake
```

Latest `main` prerelease:

```bash
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s -- --app Snake --channel nightly
```

Stable release on Windows PowerShell:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1))) -App Snake
```

Latest `main` prerelease on Windows PowerShell:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1))) -App Snake -Channel Nightly
```

The installers detect the operating system and architecture, download the matching Native AOT archive, and verify it against the release SHA-256 manifest. Archives for Linux, Windows, and macOS on x64 and Arm64 are also available from [GitHub Releases](https://github.com/RazorConsole/RazorConsole/releases).
