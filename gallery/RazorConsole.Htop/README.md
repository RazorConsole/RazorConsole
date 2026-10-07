# Razor Htop

An htop-style process manager built with RazorConsole. It runs on Windows, Linux and macOS, is fully mouse-aware, and is published as a Native AOT binary (process data comes from `System.Diagnostics.Process` only, so there is no reflection or P/Invoke).

```bash
dotnet run --project gallery/RazorConsole.Htop/RazorConsole.Htop.csproj -f net10.0
```

## Features

- CPU and memory meters, task and thread counts (CPU is per-core, so 100% equals one busy core)
- Sortable columns: PID, Name, CPU%, MEM, THR, TIME+
- Group by process name; groups show the summed metrics and expand into their members
- Live filter by name or PID
- Kill a process or a whole group, always behind a confirmation

## Mouse

- Click a column header to sort (click again to reverse)
- Click a row to select it; click a group row to expand or collapse it
- Click the red `Kill` button shown on the selected row (or `F9 Kill` below) and confirm with `Yes`
- Mouse wheel scrolls the list; footer buttons filter, group, pause and quit

## Keyboard

| Key | Action |
| --- | --- |
| `↑` `↓` `PgUp` `PgDn` `Home` `End` | Move selection |
| `G` | Toggle grouping by name |
| `→` / `Enter`, `←` | Expand / collapse a group |
| `F9`, `Delete` | Kill selected process or group (then `y` / `n`) |
| `/`, `F4` | Filter (`Enter` accepts, `Esc` clears) |
| `C` `M` `P` `N` `T` | Sort by CPU, memory, PID, name, time |
| `Space` | Pause or resume refreshing |
| `Q`, `Esc`, `F10` | Quit |

Killing system processes requires sufficient privileges; failures are shown in the status line.

## Publish a Native AOT binary

```bash
dotnet publish gallery/RazorConsole.Htop/RazorConsole.Htop.csproj -c Release -f net10.0 -r linux-x64
dotnet publish gallery/RazorConsole.Htop/RazorConsole.Htop.csproj -c Release -f net10.0 -r osx-arm64
dotnet publish gallery/RazorConsole.Htop/RazorConsole.Htop.csproj -c Release -f net10.0 -r win-x64
```

Install a released binary with `install-razor-console-app.sh --app Htop` (or the PowerShell equivalent), as described in the [Snake README](../RazorConsole.Snake/README.md).
