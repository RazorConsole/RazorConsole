# Codex terminal harness reference

Reference: the locally installed `codex-cli 0.157.0`, official source tag
`rust-v0.157.0`, commit `00c972ed5d6ff6499317fd41b7f23605b8e6850d`.
The real application UI is shared across live and scripted sessions. In
`--mock` mode only the session is replaced; no tools are executed and no model
is contacted. Component names and framework behavior are production code.

## Visual reference

Use the upstream `codex-rs/tui/src` snapshots as the reference: rounded startup
card, `›` user prompt, `•` assistant/tool entries, indented `└` output, an unboxed
composer, muted shortcut/context footer. Avoid a dashboard/sidebar layout for
the main conversation. Compare fixed 80x24, 120x40 and narrow 40x20 viewports.

## Mouse acceptance inventory

These are requirements, not claims of implemented support. Each needs a test
through the production input path as well as the headless terminal.

| Surface | Behavior | Upstream implementation |
| --- | --- | --- |
| Transcript | Wheel up/down, three rows per event, preserve position during streaming | `transcript_view/input.rs` |
| Transcript | Left drag selects text; drag outside viewport scrolls | `transcript_view/selection.rs` |
| Transcript | Double click selects word; triple click selects logical line | `text_selection.rs` |
| Transcript | Shift-click extends selection | `transcript_view/input.rs` |
| Transcript | Right click copies selected text | `transcript_view/input.rs` |
| Transcript | Stationary link click opens link; dragging cancels opening; Ctrl/Super-click opens directly | `transcript_view/input.rs` |
| Transcript | Click disclosure to expand/collapse tool output | `transcript_view/input.rs` |
| Transcript | Back-to-bottom hover/press/release, cancel on release outside | `transcript_view/follow_control.rs` |
| Composer | Click positions cursor; drag selects; double/triple click selects word/line | `bottom_pane/textarea/mouse.rs` |
| Composer | Drag beyond viewport advances wrapped rows | `bottom_pane/textarea/mouse.rs` |
| Composer | Typing/deleting replaces selection; right click copies while preserving draft | `bottom_pane/chat_composer/mouse.rs` |
| Footer | Click warning notice opens warnings | `chatwidget/warnings.rs` |
| Usage | Click report tabs and available range/group/model/metric/dashboard/plan/zero-credit controls | `analytics/mouse.rs`, `analytics/controls.rs` |
| Usage | Wheel scrolls body only, independent per view | `analytics/mouse.rs` |

Native terminal selection/copy when mouse capture is disabled is a separate
terminal behavior, not evidence that the application handles these gestures.
Tests record clipboard/link effects at the external side-effect boundary.
The application uses Core terminal actions for real copy and URL launching.

## Verification boundaries

The transcript now uses real Core selectable styled text: padded user surfaces,
Markdown emphasis/code/link styles, tool disclosures, red errors and hanging
soft-wrap indentation. The permission picker uses a two-column layout at wide
widths and stacked descriptions at narrow widths. Cell/style assertions cover
these features. These are not screenshot-level pixel-difference assertions;
terminal font metrics, native wrapping/scrollback, complete Markdown rendering,
and long-history startup-card scrolling are not yet at full reference parity.
The permission labels deliberately describe this application's actual policy,
not Codex's sandbox or unsafe-action classifier, which are not implemented here.

Slash-command selection now has unboxed two-column rows, full-row blue selection,
prefix filtering, arrows, Tab completion, Enter/click execution and hover/wheel
selection. Pipe tables render aligned columns with gold headings and separated
rows, reflow on resize, and retain selectable inline styles. List item indentation
is consistent across siblings, nested items and wrapped continuation lines.

`tests/e2e/terminal-input-pty.py` covers the real Unix byte-input path separately
from TestTerminal. macOS PTY checks include sustained/fragmented SGR wheel reports
without draft corruption and native arrow/Tab command selection. The decoder is
used before Console.ReadKey can interpret mouse bytes; it is not a draft sanitizer.

TestTerminal paints the widget canvas in process. It verifies layout, event
dispatch and component state; it does not verify Console.ReadKey decoding,
Windows console modes, ANSI mouse reporting or a terminal emulator's rendering.
Passing in-process tests alone is insufficient to claim runtime mouse parity.

Current native evidence: a macOS PTY ran the real application, received SGR mouse
reports, placed the composer cursor at the clicked position and changed `hello`
to `Zhello` after keyboard input. Windows native mouse input, terminal-specific
clipboard confirmation, stationary out-of-bounds drag autoscroll and complete
Usage dashboard semantics still need validation/implementation before claiming
full Codex 0.157.0 parity.
