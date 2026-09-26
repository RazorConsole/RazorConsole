"""Gallery wheel-storm regression against a real Unix PTY (no tmux).

Build Gallery in Release, then:
  uv run --with pyte python tests/e2e/gallery-scroll-pty.py --dotnet /path/to/dotnet

Deliberately never answer cursor-position queries. The old renderer stalls here.
"""

import argparse
import codecs
import fcntl
import os
from pathlib import Path
import pty
import select
import shutil
import signal
import struct
import subprocess
import termios
import time

import pyte


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default=shutil.which("dotnet"))
    parser.add_argument("--framework", default="net10.0")
    parser.add_argument("--configuration", choices=["Release", "Debug"], default="Release")
    args = parser.parse_args()
    if not args.dotnet:
        parser.error("Pass --dotnet /path/to/dotnet")
    root = Path(__file__).resolve().parents[2]
    app = root / f"artifacts/bin/RazorConsole.Gallery/{args.configuration.lower()}_{args.framework}/RazorConsole.Gallery.dll"
    if not app.exists():
        parser.error(f"Build Gallery -c Release -f {args.framework} first")
    master, slave = pty.openpty()
    os.set_blocking(master, False)
    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 35, 100, 0, 0))

    def setup():
        os.setsid()
        fcntl.ioctl(0, termios.TIOCSCTTY, 0)

    process = subprocess.Popen(
        [args.dotnet, str(app)], cwd=root, env=dict(os.environ, TERM="xterm-256color"),
        stdin=slave, stdout=slave, stderr=slave, preexec_fn=setup,
    )
    screen = pyte.Screen(100, 35)
    stream = pyte.Stream(screen)
    decoder = codecs.getincrementaldecoder("utf-8")("replace")
    output_tail = ""

    def frame():
        return "\n".join(screen.display)

    def drain(seconds):
        nonlocal output_tail
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            if not select.select([master], [], [], 0.01)[0]:
                continue
            try:
                data = os.read(master, 65536)
            except BlockingIOError:
                continue
            except OSError:
                break
            content = decoder.decode(data)
            combined = output_tail + content
            assert "\x1b[6n" not in combined, "Renderer queried cursor position"
            assert "\x1b[3J" not in combined, "Renderer erased scrollback"
            output_tail = combined[-16:]
            stream.feed(content.replace("\x1bE", "\r\n"))

    def send(data):
        deadline = time.monotonic() + 3
        while data:
            try:
                data = data[os.write(master, data):]
            except BlockingIOError:
                assert time.monotonic() < deadline, "Input blocked\n" + frame()
                drain(0.01)

    def expect(text, timeout=10):
        deadline = time.monotonic() + timeout
        while text not in frame() and time.monotonic() < deadline:
            drain(0.02)
        assert text in frame(), f"Missing {text}\n{frame()}"

    def click(text, minimum=0):
        for row, line in enumerate(screen.display):
            column = line.find(text, minimum)
            if column >= 0:
                send(f"\x1b[<0;{column+1};{row+1}M\x1b[<0;{column+1};{row+1}m".encode())
                drain(0.15)
                return
        raise AssertionError(f"Missing clickable {text}\n{frame()}")

    try:
        expect("Hello, layout!")
        click("Scrollable")
        expect("Embedded scrollbar")
        click("Code", 27)
        expect("AlphabetData")
        # Burst writes let input and rendering overlap; do not serialize per event.
        for batch in range(40):
            code = 65 if batch % 2 == 0 else 64
            send(f"\x1b[<{code};50;15M".encode() * 25)
            if batch == 19:
                fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 24, 80, 0, 0))
                screen.resize(24, 80)
                process.send_signal(signal.SIGWINCH)
            drain(0.01)
        # Delayed/stale replies must not be interpreted as keys.
        send(b"\x1b[12;")
        drain(0.1)
        send(b"40R")
        send(b"\x1b[<64;50;15M" * 100)
        # Search stays at a fixed position while the main pane scrolls. Its
        # focused placeholder is a FIFO barrier: all queued wheel events have
        # been handled before this click. Text in the old Code frame is not a
        # reliable indication that a burst has finished on a slower CI runner.
        click("Search")
        expect("Type to search", timeout=60)
        send(b"border")
        expect("border")
        click("Border")
        expect("Compare border styles")
        send(b"\x03")
        deadline = time.monotonic() + 3
        while process.poll() is None and time.monotonic() < deadline:
            drain(0.02)
        assert process.poll() is not None, "Ctrl+C did not exit"
        print("PASS: 1000 wheel packets + resize + stale reply; click, typing and exit remain responsive; no cursor queries or scrollback erase")
    finally:
        if process.poll() is None:
            process.terminate()
        try:
            process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
        os.close(master)
        os.close(slave)


if __name__ == "__main__":
    main()
