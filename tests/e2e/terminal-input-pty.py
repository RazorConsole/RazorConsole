"""Exercise real Unix terminal input with the offline LLMAgentTUI session.

Build LLMAgentTUI in Release/net10.0, then run:
  uv run --with pyte python tests/e2e/terminal-input-pty.py
No tmux, provider requests, API keys, or real tool execution are used.
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
    parser.add_argument("--framework", choices=["net8.0", "net9.0", "net10.0", "net11.0"], default="net10.0")
    args = parser.parse_args()
    if not args.dotnet:
        parser.error("dotnet is not on PATH; pass --dotnet /path/to/dotnet")
    root = Path(__file__).resolve().parents[2]
    app = root / f"artifacts/bin/LLMAgentTUI/release_{args.framework}/LLMAgentTUI.dll"
    if not app.exists():
        parser.error(f"build examples/LLMAgentTUI -c Release -f {args.framework} first")
    master, slave = pty.openpty()
    os.set_blocking(master, False)
    fcntl.ioctl(slave, termios.TIOCSWINSZ, struct.pack("HHHH", 35, 87, 0, 0))
    env = dict(os.environ, TERM="xterm-256color")

    def setup():
        os.setsid()
        fcntl.ioctl(0, termios.TIOCSCTTY, 0)

    process = subprocess.Popen(
        [args.dotnet, str(app), "--mock"], cwd=root, env=env,
        stdin=slave, stdout=slave, stderr=slave, preexec_fn=setup,
    )
    screen = pyte.Screen(87, 35)
    stream = pyte.Stream(screen)
    utf8 = codecs.getincrementaldecoder("utf-8")("replace")
    pending = ""
    mouse_disabled = False

    def frame():
        return "\n".join(screen.display)

    def drain(seconds):
        nonlocal pending, mouse_disabled
        end = time.monotonic() + seconds
        while time.monotonic() < end:
            if not select.select([master], [], [], 0.01)[0]:
                continue
            try:
                data = os.read(master, 65536)
            except (BlockingIOError, OSError):
                continue
            content = pending + utf8.decode(data)
            pending = ""
            mouse_disabled |= "\x1b[?1003l\x1b[?1006l" in content
            if "\x1b[6n" in content:
                os.write(master, b"\x1b[1;1R")
            if content.endswith("\x1b"):
                content, pending = content[:-1], "\x1b"
            # VT NEL resets column to zero; pyte implements ESC E as LF only.
            stream.feed(content.replace("\x1bE", "\r\n"))

    def send(data):
        deadline = time.monotonic() + 3
        while data:
            try:
                data = data[os.write(master, data):]
            except BlockingIOError:
                assert time.monotonic() < deadline, "PTY input stalled\n" + frame()
                drain(0.02)

    def expect(text):
        deadline = time.monotonic() + 3
        while text not in frame() and time.monotonic() < deadline:
            drain(0.02)
        assert text in frame(), frame()

    try:
        expect("LLMAgentTUI")
        send(b"hello")
        expect("› hello")
        for _ in range(100):
            send(b"\x1b[<64;10;10M\x1b[<65;10;10M")
            drain(0.01)
        # Fragmented packet with pauses longer than the old parser deadline.
        send(b"\x1b[<65;")
        drain(0.15)
        send(b"10;10M!")
        expect("› hello!")
        assert "<;M" not in frame(), frame()
        print("PASS: 201 wheel packets (including delayed fragments) preserve draft")
        send(b"\x7f" * 6 + b"/")
        expect("choose what LLMAgentTUI")
        send(b"\x1b[B\t")
        expect("› /usage")
        print("PASS: native arrow and Tab complete the selected slash command")
        send(b"\x03")
        drain(0.7)
        assert mouse_disabled, "Ctrl+C did not disable mouse reporting"
        # macOS invalidates tcgetattr on the slave after the session leader exits.
        # Check observable teardown, not settings on an already hung-up PTY.
        print("PASS: Ctrl+C disables terminal mouse reporting")
    finally:
        if process.poll() is None:
            process.send_signal(signal.SIGINT)
        drain(0.3)
        os.close(master)
        os.close(slave)
        try:
            process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            process.kill()


if __name__ == "__main__":
    main()
