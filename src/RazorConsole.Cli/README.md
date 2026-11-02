# RazorConsole.Cli

`razorconsole` drives [RazorConsole](https://github.com/RazorConsole/RazorConsole) apps from the command line. It starts an app in a pseudoterminal, presses keys and clicks, waits for the screen to reach a state, and reads the app's component tree, focus, route, errors and logs. Results can be JSON with fixed exit codes, for shell scripts, CI jobs and AI agents.

## Install

```bash
dotnet tool install --global RazorConsole.Cli --prerelease
razorconsole doctor
```

Needs .NET 8 or newer. Sessions run in ConPTY on Windows 10 version 1809 or later, and in a POSIX PTY on Linux and macOS.

Each [GitHub release](https://github.com/RazorConsole/RazorConsole/releases) also has a Native AOT build as `razorconsole-<version>-<platform>.zip` (Windows) or `.tar.gz` (Linux and macOS), for x64 and arm64. Unpack it and run `razorconsole`; it needs no .NET runtime of its own, though the apps it runs (`--project`, `--dll`) still do.

## Use

```bash
razorconsole start demo --project ./MyApp   # build, run, wait for the first frame
razorconsole wait --text "Welcome"
razorconsole tree --depth 2
razorconsole click --element hook:save      # a real mouse click on the element
razorconsole assert --text "clicked: 1"     # exit 1 if it does not hold
razorconsole screen
razorconsole stop
```

The terminal commands work with any program. The app commands (`tree`, `find`, `inspect`, `focus`, `route`, `errors`, `logs`, `stats`, and `--element` selectors) need the app's diagnostics endpoint, which a RazorConsole app registers only when its host environment is `Development` or its entry assembly is a Debug build. Release builds never do.

## When an app crashes

A crash ends the app's process and its diagnostics endpoint with it, so `errors` and `logs` cannot be read afterwards. Every later command fails with exit 6 (`process_exited`), and the error carries the exit code, `outputTail` (the end of the terminal output as plain text, where .NET prints an unhandled exception) and `lastErrors` (the last errors the app reported before it went away). In text mode they are printed after the message.

## Documentation

The full guide, with every command, the selectors, the JSON output and the exit codes: <https://razorconsole.com/blog/cli/>

## License

MIT
