---
name: razorconsole-example-alignment
description: "Use this skill whenever aligning RazorConsole examples between LegacySpectre and WidgetLayout, adding --widget-layout or RAZORCONSOLE_RENDERING_PIPELINE switches, comparing console rendering parity, fixing WidgetLayout example regressions, creating focused widget regression tests, or updating example README smoke-test instructions. Trigger even when the user casually says to align, compare, dogfood, smoke-test, or make an example work with WidgetLayout."
---

# RazorConsole Example Alignment

Use this workflow to align a RazorConsole example so it behaves and renders correctly under both the legacy Spectre pipeline and the native WidgetLayout pipeline.

## Goal

Drive example alignment through real app output, but turn repeated visual mismatches into reusable core fixes whenever possible. Examples are dogfood for the layout engine: if several samples expose the same mismatch, fix `RazorConsole.Core` widgets or `WidgetTranslationContext` instead of patching each example with local layout hacks.

## Alignment loop

1. **Inspect the example shape**
   - Read the example's `Program.cs`, main `.razor` component(s), `.csproj`, and `README.md`.
   - Identify component families in use: `Rows`, `Columns`, `Panel`, `Align`, `Padder`, `Figlet`, `TextInput`, `SpectreTable`, `Scrollable`, `TextButton`, `Markdown`, or custom services.
   - Check whether the example already has a WidgetLayout switch and README instructions.

2. **Add the WidgetLayout switch when missing**
   - Add `ConsoleAppOptions.RenderingPipeline = ResolveRenderingPipeline(args)`.
   - Support both `--widget-layout` and `RAZORCONSOLE_RENDERING_PIPELINE=WidgetLayout` or `widget`.
   - Preserve existing options such as `EnableTerminalResizing` and `AutoClearConsole`.

3. **Build before comparing output**
   - Run a focused build for the example with `-f net10.0` in this repository.
   - Fix compile errors before visual work.

4. **Capture legacy and WidgetLayout output**
   - Run the example once without `--widget-layout` for legacy output.
   - Run it again with `--widget-layout` for WidgetLayout output.
   - Prefer short smoke runs that are long enough to render the first frame, then stop/kill any persistent terminal session.
   - Compare structure first: title, panels, rows/columns, widths, wrapping, table borders, footer/header placement, focus colors, and error rendering.

5. **Classify every mismatch**
   - **Translator gap**: `WidgetTranslationContext` is not producing an equivalent widget tree.
   - **Widget layout gap**: native widget measure/arrange/paint differs from Spectre behavior.
   - **Example design issue**: example relies on Spectre layout coupling that should be redesigned for WidgetLayout.
   - **Provider/runtime issue**: example talks to external services and needs clearer error rendering or README guidance.

6. **Fix generic causes first**
   - Prefer core fixes in `src/RazorConsole.Core/Layout/` and `WidgetTranslationContext` when a mismatch reflects shared semantics.
   - Use example-specific markup changes only when the sample design itself is the problem.
   - Keep legacy behavior working while improving WidgetLayout.

7. **Add regression coverage**
   - Add focused unit tests in `src/RazorConsole.Tests/Layout/WidgetLayoutTests.cs` for widget/translator behavior.
   - Add renderer/accessor tests only when the pipeline, focus, diagnostics, or layout snapshot behavior changes.
   - Keep tests deterministic by rendering widgets to text instead of relying on a live terminal.

8. **Update docs**
   - Update the example README with the WidgetLayout run command.
   - Add troubleshooting notes when alignment exposes expected external setup, such as Ollama needing `ollama serve`.

9. **Validate**
   - Run focused tests first for the changed area.
   - Run the full RazorConsole test project with `-f net10.0` after core layout changes.
   - Build the aligned example.
   - Smoke-run WidgetLayout output once more and confirm the original mismatch is gone.

## Common fixes from previous alignments

- **Figlet or Spectre leaf content is clipped**: check `SpectreWidget` measurement/paint width. Some Spectre renderables need painting at the measured max width, not only the final child bounds.
- **Unexpected missing blank lines around Figlet**: do not trim trailing blank render lines if legacy output preserves them.
- **TextInput label and placeholder split incorrectly**: translate TextInput into an inline row-like structure with label plus padded display content.
- **Footer or centered text is left-biased**: `AlignWidget` should measure to available width by default for center/right alignment when explicit width is omitted.
- **Long footer text clips**: `TextWidget` should wrap by words and paint within arranged bounds.
- **Fixed-width table becomes full width**: parent stacks should preserve a child's desired width during arrange instead of forcing parent width.
- **Table alignment differs**: inspect `TableWidget` column sizing, cell padding, header/body/footer row boxes, and left/center/right cell alignment.
- **Scrollable table semantics are confusing**: Spectre `Scrollable` with embedded table scrolling is not orthogonal. Prefer a pageable example design or a non-embedded `ScrollableWidget` until table-body scrolling is intentionally designed.
- **Diagnostics cannot find focused layout**: use stable hooks such as `data-focus-key` or `data-vnode-hook`; do not rely on `VNode.ID` across renders.
- **Literal markup appears in Markdown output**: error messages intended to be styled should render as `Markup` with color instead of embedding Spectre markup inside Markdown content.

## Validation commands

Adapt paths to the example under alignment.

- Focused example build: `dotnet build examples/<Example>/<Example>.csproj -f net10.0`
- Legacy smoke run: `dotnet run --project examples/<Example>/<Example>.csproj -f net10.0`
- WidgetLayout smoke run: `dotnet run --project examples/<Example>/<Example>.csproj -f net10.0 -- --widget-layout`
- Focused tests: `dotnet test src/RazorConsole.Tests/RazorConsole.Tests.csproj -f net10.0 --filter <TestNameOrClass>`
- Full tests after core layout changes: `dotnet test src/RazorConsole.Tests/RazorConsole.Tests.csproj -f net10.0`

## Definition of done

An aligned example is done when:

- The example builds under `net10.0`.
- Legacy and WidgetLayout first-frame output are acceptably similar or the difference is intentionally documented.
- Any reusable WidgetLayout behavior fix has a regression test.
- The example README documents how to run WidgetLayout.
- Full tests pass when core layout code was changed.
- No long-running smoke terminal is left alive.

## Anti-patterns

- Do not make one-off example spacing hacks when a native widget is measuring or arranging incorrectly.
- Do not use `VNode.ID` as stable identity across render frames.
- Do not skip legacy output comparison; WidgetLayout parity needs a baseline.
- Do not leave external-service errors as raw exception or markup text when a user-facing hint would make the sample usable.
- Do not broaden the WidgetLayout default behavior just to satisfy one example without adding a regression test explaining the invariant.
