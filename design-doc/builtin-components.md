# Built-in RazorConsole Components

This reference summarizes the Razor components that ship with RazorConsole. Each component projects to a Spectre.Console renderable at runtime. When composing components, treat them as immutable outside of their rendering lifecycle and prefer configuring them through parameters.

> **Tip:** Components marked with `RenderFragment? ChildContent` accept arbitrary nested markup.

Current catalog: `Align`, `Border`, `Columns`, `Figlet`, `Grid`, `Markup`, `Newline`, `Padder`, `Panel`, `Rows`, `Select`, `Spinner`, `SyntaxHighlighter`, `Table`, `TextButton`, `TextInput`.

## Align
Wraps child content in an alignment container.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | — | Nested content to align. |
| `Horizontal` | `HorizontalAlignment` | `Left` | Horizontal alignment (`Left`, `Center`, `Right`). |
| `Vertical` | `VerticalAlignment` | `Top` | Vertical alignment (`Top`, `Middle`, `Bottom`). |
| `Width` | `int?` | `null` | Fixed width in characters when greater than zero. |
| `Height` | `int?` | `null` | Fixed height in rows when greater than zero. |

## Border
Creates a bordered panel around its children.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | — | Content rendered inside the border. |
| `BorderColor` | `Color?` | `null` | Optional border color. |
| `BoxBorder` | `BoxBorder` | `Rounded` | Border style (`Rounded`, `Square`, `Double`, etc.). |
| `Padding` | `Padding` | `new(0,0,0,0)` | Inner padding inside the border. |

## BarChart
Renders a beautiful horizontal bar chart using provided data.

| Parameter           | Type                      | Default                  | Description                                                                                                                |
|---------------------|---------------------------|--------------------------|----------------------------------------------------------------------------------------------------------------------------|
| `BarChartItems`     | `List<IBarChartItem>`     | —                        | Collection of data items. Each item must have `Label` and `Value`. Optional `Color` (as `Spectre.Console.Color`). Required. |
| `Width`             | `int?`                      | `null`                   | Chart width in characters. If omitted — uses full available console width.                                                 |
| `Label`             | `string?`                   | `null`                   | Title displayed above the chart.                                           |
| `LabelForeground`   | `Color`                     | `Style.Plain.Foreground` | Text color of the label (default: white/terminal default).                                                                 |
| `LabelBackground`   | `Color`                     | `Style.Plain.Background` | Background color of the label (default: transparent).                                                                      |
| `LabelDecoration`   | `Decoration`                | `Decoration.None`        | Label style: `Bold`, `Italic`, `Underline`, etc.                                                              |
| `LabelAlignment`    | `Justify?`                  | `null`                   | Label alignment: `Left`, `Center`, or `Right`.                                                                             |
| `MaxValue`          | `double?`                   | `null`                   | Fixed maximum value for scaling (e.g. set to `100` for percentage-style charts).                                           |
| `ShowValues`        | `bool`                      | `false`                  | If `true` — shows numeric values next to each bar (e.g. `42.3`).                                                           |
| `Culture`           | `CultureInfo`             | `CultureInfo.CurrentCulture` | Culture used to format numbers.                                          |

## BreakdownChart
Renders a colorful breakdown (pie-style) chart using provided data.

| Parameter                        | Type                        | Default                      | Description                                                                              |
|----------------------------------|-----------------------------|------------------------------|------------------------------------------------------------------------------------------|
| `BreakdownChartItems`            | `List<IBreakdownChartItem>` | —                            | Collection of data items. Each item must have `Label`, `Value`, and `Color`. Required.   |
| `Compact`                        | `bool`                      | `false`                      | If `true`, renders the chart and tags in compact mode with reduced spacing.              |
| `Culture`                        | `CultureInfo`               | `CultureInfo.CurrentCulture` | Culture used to format numbers and percentages.                                          |
| `Expand`                         | `bool`                      | `false`                      | If `true`, the chart expands to fill all available horizontal space.                     |
| `Width`                          | `int?`                      | `null`                       | Fixed width of the chart in characters. If `null`, width is calculated automatically.    |
| `ShowTags`                       | `bool`                      | `false`                      | If `true`, displays a legend with colored tags below the chart.                          |
| `ShowTagValues`                  | `bool`                      | `false`                      | If `true`, shows absolute values next to tags (e.g. `1,234`).                            |
| `ShowTagValuesPercentage`        | `bool`                      | `false`                      | If `true`, shows percentage values next to tags (e.g. `42.3%`).                          |
| `ValueColor`                     | `Color?`                    | `null`                       | Color used for numeric values in tags. If `null`, uses default console foreground color. |

## Columns
Flow child renderables in Spectre.Console columns.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | — | Column items. |
| `Expand` | `bool` | `false` | When `true`, forces columns to fill the available width. |

## Figlet
Renders large ASCII art text using Figlet fonts.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Content` | `string` | `string.Empty` | Text to render. |
| `Justify` | `Justify` | `Center` | Alignment inside the Figlet block. |
| `Color` | `Color` | `Color.Default` | Foreground color for the glyphs. |

## Grid
Builds a Spectre.Console grid with configurable columns.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | — | Grid rows and cells. |
| `Columns` | `int` | `2` | Number of columns in the grid. |
| `Expand` | `bool` | `false` | Stretch grid to available width. |
| `Width` | `int?` | `null` | Fixed width when greater than zero. |

## Markup
Outputs Spectre markup with styling.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Content` ⚠️ | `string` | — | Text to render; automatically escaped. Required. |
| `Foreground` | `Color` | `Style.Plain.Foreground` | Text color. |
| `Background` | `Color` | `Style.Plain.Background` | Background color. |
| `Decoration` | `Decoration` | `None` | Styling flags (`Bold`, `Italic`, etc.). |
| `link` | `string?` | `null` | Optional hyperlink target. |

## Newline
Emits a single line break via `<Markup>` with `Environment.NewLine`. No parameters.

## Spectre Canvas 
SpectreCanvas component renders an array of pixels with different colors.

| Parameter         | Type                            | Default | Description                                                                                                        |
|-------------------|---------------------------------|---------|--------------------------------------------------------------------------------------------------------------------|
| `Pixels` ⚠️       | `(int x, int y, Color color)[]` | —       | Pixels to render;  Required.                                                                                       |
| `CanvasWidth` ⚠️  | `int`                           | —       | Canvas width; Required.                                                                                            |
| `CanvasHeight` ⚠️ | `int`                           | —       | Canvas height; Required.                                                                                           |
| `MaxWidth`        | `int?`                          | `null`    | Max width of the canvas.                                                                                           |
| `PixelWidth`      | `int`                           | `2`     | Number of rectangles that will render as a pixel. One rectangle's width is half of square, so default value is 2. |
| `Scale`           | `bool`                          | `false` | The value indicating whether or not to scale the canvas when rendering.                                            |

## Padder
Adds padding around nested content.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | — | Inner content. |
| `Padding` | `Padding` | `new(0,0,0,0)` | Padding thickness (left, top, right, bottom). |

## Panel
Full-featured Spectre panel wrapper.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | — | Panel body content. |
| `Title` | `string?` | `null` | Optional panel header text. |
| `TitleColor` | `Color?` | `null` | Color for the header text. |
| `BorderColor` | `Color?` | `null` | Panel border color. |
| `Border` | `BoxBorder?` | `null` | Specific border style; defaults to Spectre's standard when `null`. |
| `Height` | `int?` | `null` | Fixed height when positive. |
| `Padding` | `Padding?` | `null` | Insets between border and content. |
| `Width` | `int?` | `null` | Fixed width when positive. |
| `Expand` | `bool` | `false` | Stretch panel to available width. |

## Rows
Stacks child renderables vertically.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `ChildContent` | `RenderFragment?` | — | Row items. |
| `Expand` | `bool` | `false` | When `true`, rows fill the available height. |

## Select
Interactive dropdown for choosing a value. Integrates with the focus and keyboard systems.

**Core parameters**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Options` | `string[]` | `Array.Empty<string>()` | Available options. |
| `Value` | `string?` | `null` | Current selection. |
| `ValueChanged` | `EventCallback<string>` | — | Raised on selection change. |
| `OnSelected` | `EventCallback<string?>` | — | Fired after a user confirms an option. |
| `OnClear` | `EventCallback` | — | Fired when selection is cleared. |
| `Placeholder` | `string` | `"Select an option"` | Placeholder when no selection is set. |
| `Expand` | `bool` | `false` | Stretch the control horizontally. |
| `FocusOrder` | `int?` | `null` | Order key for focus navigation. |
| `BorderStyle` | `BoxBorder` | `Rounded` | Border style for the input panel. |

**Appearance parameters**

| Parameter | Role | Default |
|-----------|------|---------|
| `PlaceholderColor` / `Decoration` | Styling when no selection exists. | `Grey70`, `Italic | Dim` |
| `ValueColor` / `Decoration` | Styling for selected value. | `White`, `None` |
| `EmptyLabel` / `EmptyForeground` / `EmptyDecoration` | Displayed when `Options` is empty. | `"No options available"`, `Grey70`, `Italic` |
| `OptionForeground` / `OptionDecoration` | Styling for list options. | `White`, `None` |
| `SelectedOptionForeground` / `SelectedOptionDecoration` | Highlight for the focused option. | `Chartreuse1`, `Bold` |

Keyboard navigation supports arrows, <kbd>Space</kbd> to toggle, <kbd>Enter</kbd> to commit, <kbd>Escape</kbd> to cancel, and type-ahead letters for quick jumps.

## Spinner
Shows a Spectre spinner with optional message.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `SpinnerType` | `Spinner` | `Spinner.Known.Dots` | Spinner instance to render. |
| `SpinnerName` | `string?` | `null` | Explicit spinner name override. |
| `Message` | `string?` | `null` | Optional message displayed alongside the spinner. |
| `Style` | `string?` | `null` | Spectre style markup for the spinner and message. |
| `AutoDismiss` | `bool` | `false` | Remove spinner automatically when completed. |

## Scrollable
Renders a limited portion of a collection (`PageSize`) and enables keyboard scrolling.

| Parameter | Type                                   | Default                | Description |
|-----------|----------------------------------------|------------------------|-------------|
| `Items` | `IReadOnlyList<TItem>`                 | `Array.Empty<TItem>()` | Full data source. |
| `PageSize` | `int`                                  | `1`                    | Items shown at once. |
| `ChildContent` | `RenderFragment<ScrollContext<TItem>>` | —                      | Markup for the visible page. |
| `ScrollOffset` | `int`                                  | `0`                    | Two-way – start index of current page. |
| `ScrollOffsetChanged` | `EventCallback<int>`                   | —                      | Fired when offset changes. |
| `IsScrollbarEmbedded` | `bool`                                 | `true`                  | Determines whether the scrollbar should be visually embedded within the border of a `Table` or `Panel`/`Border` components. |

### `ScrollContext<TItem>`
Context to get access with paginated items, keyboard event and other info.

| Member | Type | Description |
|--------|------|-------------|
| `this[int i]` | `TItem` | Visible item at index `i`. |
| `Count` | `int` | Visible items count. |
| `GetEnumerator()` | — | Enables `foreach`. |
| `KeyDownEventHandler` | `Func<KeyboardEventArgs,Task>` | Attach via `@onkeydown`. |
| `CurrentOffset` | `int` | Same as `ScrollOffset`. |
| `PagesCount` | `int` | Total pages: `PageSize >= Items.Count ? 1 : Items.Count - PageSize + 1`. |

### `ScrollbarSettings`
Record, that enables and adjusts scrollbar inside the Scrollable component.

| Parameter | Type | Default | Description                                                                                |
|-----------|------|---------|--------------------------------------------------------------------------------------------|
| `TrackChar` | `char` | `'│'` | Character used for the scrollbar track.                                                    |
| `ThumbChar` | `char` | `'█'` | Character used for the scrollbar thumb.                                                    |
| `TrackColor` | `Color` | `Color.Grey` | Color of the track in normal state.                                                        |
| `ThumbColor` | `Color` | `Color.White` | Color of the thumb in normal state.                                                        |
| `TrackFocusedColor` | `Color` | `Color.Grey74` | Color of the track when focused.                                                           |
| `ThumbFocusedColor` | `Color` | `Color.DeepSkyBlue1` | Color of the thumb when focused.                                                           |
| `MinThumbHeight` | `int` | `1` | Minimum height of the thumb in characters.                                                 |
| `OnFocusInCallback` | `Action<FocusEventArgs>?` | `null` | Invoked when scrollbar gains focus.                                                        |
| `OnFocusOutCallback` | `Action<FocusEventArgs>?` | `null` | Invoked when scrollbar loses focus.                                                        |

## StepChart

Renders a step chart directly in the terminal using Unicode box-drawing characters. Perfect for visualizing time-series, discrete state changes, or any data where values stay constant between points.

| Parameter       | Type                          | Default      | Description                                                 |
|-----------------|-------------------------------|--------------|-------------------------------------------------------------|
| `Width`         | `int`                         | `60`         | Chart width in terminal columns (excluding axes if shown)   |
| `Height`        | `int`                         | `20`         | Chart height in terminal rows (excluding title/axes)        |
| `ShowAxes`      | `bool`                        | `true`       | Shows X/Y axes with tick marks and numeric labels           |
| `AxesColor`     | `Color`                       | `Color.Grey` | Color of the axis lines and tick marks                      |
| `LabelsColor`   | `Color`                       | `Color.Grey` | Color of the numeric value labels on the axes               |
| `Title`         | `string?`                     | `null`       | Optional chart title displayed above the plot area          |
| `TitleColor`    | `Color`                       | `Color.Grey` | Color of the title text                                     |
| `Series`        | `List<ChartSeries>`           | —            | Collection of data series to render on the chart. Required. |

### `ChartSeries`
Record that stores series data:

| Member  | Type                         | Description  |
|---------|------------------------------|--------------|
| `Color` | `SpectreConsole.Color`       | Line color.  |
| `Points` | `List<(double X, double Y)>` | Data points. |


## SyntaxHighlighter
Renders highlighted code blocks with `SyntaxHighlightingService`.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Code` | `string` | `string.Empty` | Source text to highlight. |
| `Language` | `string?` | `null` | Language identifier (e.g., `"csharp"`). |
| `Theme` | `string?` | `null` | Theme key registered with the service. |
| `ShowLineNumbers` | `bool` | `false` | Display line numbers when `true`. |
| `Options` | `SyntaxOptions?` | `null` | Optional preconfigured syntax options. |
| `TabWidth` | `int?` | `null` | Overrides `Options.TabWidth` when > 0. |
| `AdditionalAttributes` | `IReadOnlyDictionary<string, object?>?` | `null` | Pass-through attributes for advanced scenarios. |

### Default language keys
The built-in `ColorCodeLanguageRegistry` ships with the following language keys (case-insensitive). Aliases map to the same syntax highlighter:

- `text`, `plaintext`, `plain`
- `csharp`, `cs`
- `razor`
- `html`
- `json`
- `xml`
- `sql`
- `js`, `javascript`
- `ts`, `typescript`
- `css`
- `powershell`, `ps`
- `python`
- `md`, `markdown`

Register additional languages via `ISyntaxLanguageRegistry.Register` during application startup.

## Table
Turns semantic HTML table markup into a Spectre.Console `Table` renderable.

> **Key idea:** Author tables with standard `<table>`, `<thead>`, `<tbody>`, `<tfoot>`, `<tr>`, `<th>`, and `<td>` tags. RazorConsole extracts structure and styling hints from attributes instead of introducing bespoke child components.

| Attribute | Type | Default | Description |
|-----------|------|---------|-------------|
| `class="table"` ⚠️ | — | — | Required hook so the translator recognizes the element. |
| `data-expand` | `bool` | `false` | Stretch the rendered table to the available console width. |
| `data-width` | `int?` | `null` | Fixed overall width in characters when greater than zero. |
| `data-title` | `string?` | `null` | Optional caption rendered above the table. |
| `data-border` | `TableBorderStyle` | `None` | Spectre border style (`None`, `Square`, `Rounded`, `Heavy`, etc.). |
| `data-show-headers` | `bool` | `true` | Controls whether header rows are rendered when a `<thead>` is present. |

### Cells and rows

- Header cells use `<th>`; body cells use `<td>`. Both can contain any RazorConsole component (e.g., `<Markup>` or nested layout primitives).
- Set per-column alignment with `data-align="left|center|right"` on `<th>` elements; body rows inherit from their corresponding header.

### Example

```razor
<Table Expand="true" Title="Build status" Border="TableBorder.Heavy">
	<thead>
		<tr>
			<th data-align="left">Stage</th>
			<th data-align="center">Duration</th>
			<th data-align="right">Result</th>
		</tr>
	</thead>
	<tbody>
		<tr data-style="fg=grey">
			<td>Compile</td>
			<td>00:12:41</td>
			<td><Markup Content="[green]✔[/]" /></td>
		</tr>
		<tr data-style="fg=grey">
			<td>Tests</td>
			<td>00:24:03</td>
			<td><Markup Content="[yellow]⚠[/]" /></td>
		</tr>
	</tbody>
</Table>
```

## TextButton
Clickable text button that changes background while focused.

| Parameter | Type | Default              | Description |
|-----------|------|----------------------|-------------|
| `Content` | `string` | `string.Empty`       | Button label. |
| `BackgroundColor` | `Color` | `Color.Default`      | Background when idle. |
| `FocusedColor` | `Color` | `Color.DeepSkyBlue1` | Background when focused. |
| `OnClick` | `EventCallback` | —                    | Fired when the button is clicked. |

## TextInput
Text entry control with placeholder, masking, and focus management.

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `Value` | `string?` | `null` | Two-way bound text value. |
| `ValueChanged` | `EventCallback<string?>` | — | Raised on input changes. |
| `ValueExpression` | `Expression<Func<string?>>?` | `null` | Used by validation frameworks. |
| `OnInput` | `EventCallback<string?>` | — | Fires for every keystroke. |
| `OnSubmit` | `EventCallback<string?>` | — | Fires when the control dispatches a change event. |
| `OnFocus` / `OnBlur` | `EventCallback<FocusEventArgs>` | — | Focus lifecycle hooks. |
| `Label` | `string?` | `null` | Optional label rendered before the input. |
| `LabelColor` / `LabelDecoration` | `Color`, `Decoration` | `White`, `None` | Label styling. |
| `Placeholder` | `string?` | `null` | Placeholder text when value is empty. |
| `ValueColor` | `Color` | `White` | Foreground of typed text. |
| `PlaceholderColor` | `Color` | `Grey` | Placeholder foreground. |
| `PlaceholderDecoration` | `Decoration` | `Dim | Italic` | Placeholder styling. |
| `BorderColor` | `Color` | `Grey37` | Idle border color. |
| `FocusedBorderColor` | `Color` | `Yellow` | Border color while focused. |
| `DisabledBorderColor` | `Color` | `Grey19` | Border color when disabled. |
| `BorderStyle` | `BoxBorder` | `Rounded` | Spectre border style. |
| `BorderPadding` | `Padding` | `new(0,0,0,0)` | Padding around the panel. |
| `ContentPadding` | `Padding` | `new(1,0,1,0)` | Padding around the text content. |
| `Expand` | `bool` | `false` | Stretch to available width. |
| `Disabled` | `bool` | `false` | Disables input and focus.
| `MaskInput` | `bool` | `false` | Replace characters with bullets while displayed. |
| `FocusOrder` | `int?` | `null` | Order key for focus traversal. |
| `AdditionalAttributes` | `IReadOnlyDictionary<string, object?>?` | `null` | Arbitrary attributes forwarded to the root element. |

Keyboard handlers adjust border colors and manage focus state automatically.
