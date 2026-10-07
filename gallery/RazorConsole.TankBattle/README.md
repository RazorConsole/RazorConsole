# RazorConsole Tank Battle

A terminal tank battle game built with RazorConsole and published as a Native AOT executable.

Fight through a procedurally generated maze with responsive 3x3 tanks, destructible brick
walls, steel defenses, enemy AI, multi-level progression, and rapid-fire, spread-shot, and
homing power-ups. The maze scales to the terminal while preserving five-cell-wide passages.

## Controls

- Arrow keys or `WASD`: move
- `Space`: fire
- `P`: pause or resume
- `R`: restart
- `Q` or `Escape`: quit

## Run

```bash
dotnet run --project gallery/RazorConsole.TankBattle/RazorConsole.TankBattle.csproj -f net10.0
```

## Publish Native AOT for macOS Apple Silicon

```bash
dotnet publish gallery/RazorConsole.TankBattle/RazorConsole.TankBattle.csproj \
  -c Release -f net10.0 -r osx-arm64
```

## Install a released binary

Stable release on macOS or Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s -- --app TankBattle
```

Latest `main` prerelease:

```bash
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s -- --app TankBattle --channel nightly
```

Stable release on Windows PowerShell:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1))) -App TankBattle
```

Latest `main` prerelease on Windows PowerShell:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1))) -App TankBattle -Channel Nightly
```
