# Razor Htop

An htop-style process manager built with RazorConsole. It runs on Windows, Linux and macOS, is fully mouse-aware, and is published as a Native AOT binary (process data comes from `System.Diagnostics.Process` only, so there is no reflection or P/Invoke).

```bash
dotnet run --project gallery/RazorConsole.Htop/RazorConsole.Htop.csproj -f net10.0
```

## Features

- CPU and memory meters, task and thread counts (CPU is per-core, so 100% equals one busy core)
- Sortable columns: PID, Name, CPU%, MEM, THR, TIME+
- Group by process name; groups show the summed metrics and expand into their members
- Every process row is an expandable node: click it (or press `→`) to show path, start time, priority, handles, memory breakdown, CPU times and window title (plus command line, parent PID and state on Linux); click again or press `←` to collapse
- Live filter by name or PID
- Kill a process or a whole group, always behind a confirmation

## Mouse

- Click a column header to sort (click again to reverse)
- Click a row to select it; click a group row to expand or collapse it
- Click the red `Kill` button shown on the selected row (or `F9 Kill` below) and confirm with `Yes`
- Drag the `┃` handle at the right edge of the Name header to resize the Name column (keyboard: `[` / `]`)
- Mouse wheel scrolls the list; footer buttons filter, group, expand/collapse all, kill, pause and quit

## Keyboard

| Key | Action |
| --- | --- |
| `↑` `↓` `PgUp` `PgDn` `Home` `End` | Move selection |
| `G` | Toggle grouping by name |
| `→` / `Enter`, `←` | Expand / collapse a group |
| `F9`, `Delete` | Kill selected process or group (then `y` / `n`) |
| `/`, `F4` | Filter (`Enter` accepts, `Esc` clears) |
| `C` `M` `P` `N` `T` | Sort by CPU, memory, PID, name, time |
| `E` | Expand / collapse all (all groups in group mode, the visible rows otherwise) |
| `Space` | Pause or resume refreshing |
| `Q`, `Esc`, `F10` | Quit |

Killing system processes requires sufficient privileges; failures are shown in the status line.

## Publish a Native AOT binary

```bash
dotnet publish gallery/RazorConsole.Htop/RazorConsole.Htop.csproj -c Release -f net10.0 -r linux-x64
dotnet publish gallery/RazorConsole.Htop/RazorConsole.Htop.csproj -c Release -f net10.0 -r osx-arm64
dotnet publish gallery/RazorConsole.Htop/RazorConsole.Htop.csproj -c Release -f net10.0 -r win-x64
```

## Install a released binary

Stable release on macOS or Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s -- --app Htop
```

Latest `main` prerelease:

```bash
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s -- --app Htop --channel nightly
```

Stable release on Windows PowerShell:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1))) -App Htop
```

Latest `main` prerelease on Windows PowerShell:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1))) -App Htop -Channel Nightly
```
