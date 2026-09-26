# Terminal end-to-end tests

These tests run built RazorConsole examples inside a real pseudo-terminal. `tmux`
provides both the PTY and the terminal screen model, so the assertions observe what
a user sees after ANSI control sequences have been applied instead of matching the
raw byte stream.

The suite deliberately uses semantic assertions rather than a full-screen golden
snapshot:

- the terminal size is fixed;
- the initial screen must contain the expected controls;
- keyboard input must update visible component state;
- a burst of navigation input must settle on the expected value without killing the
  process;
- resizing must leave the application alive and render the expected content.

This complements the deterministic component and layout unit tests. It is not a
replacement for the unit tests, and it does not try to validate font rendering or a
specific terminal application's pixels.

## Why a PTY is required

Redirecting standard input and output through ordinary pipes changes how console
applications detect and use the terminal. A PTY preserves interactive terminal
behavior, including terminal dimensions, alternate-screen buffers, cursor movement,
and key sequences. Unix PTYs are described by
[`pty(7)`](https://man7.org/linux/man-pages/man7/pty.7.html); the Windows equivalent
is [ConPTY](https://learn.microsoft.com/windows/console/pseudoconsoles).

For a future all-platform harness, use ConPTY on Windows and native PTYs on Unix,
then feed output into a maintained terminal state machine. The xterm.js project
publishes a [headless terminal](https://github.com/xtermjs/xterm.js#nodejs-support)
for this purpose. The current CI job is intentionally Linux-only because the `tmux`
harness adds no runtime dependency to RazorConsole and is straightforward to debug.

## Run locally

Install `tmux`, restore the solution, and run:

```bash
dotnet build examples/Counter/Counter.csproj --configuration Release
dotnet build examples/GallerySelect/GallerySelect.csproj --configuration Release
bash tests/e2e/terminal-e2e.sh
```

On failure, the script prints the captured terminal screen and pane exit status.
