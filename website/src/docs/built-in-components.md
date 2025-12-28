# Built-in Components

RazorConsole ships with a library of Spectre.Console-powered components covering layout, input, display, and utility scenarios. You can combine them just like regular Razor components to build rich terminal experiences.

## Layout

| Component                                                                       | Highlights                                                                                                  |
| ------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------- |
| [Align](https://razorconsole.github.io/RazorConsole/components/Align)           | Centers or aligns child content horizontally/vertically with optional fixed width/height.                   |
| [Columns](https://razorconsole.github.io/RazorConsole/components/Columns)       | Flows children left-to-right, optionally expanding to fill the console width.                               |
| [Rows](https://razorconsole.github.io/RazorConsole/components/Rows)             | Stacks children vertically, great for wizard-style layouts.                                                 |
| [Grid](https://razorconsole.github.io/RazorConsole/components/Grid)             | Builds a multi-column layout with configurable column count and width.                                      |
| [Padder](https://razorconsole.github.io/RazorConsole/components/Padder)         | Adds Spectre padding around nested content to create spacing.                                               |
| [Scrollable](https://razorconsole.github.io/RazorConsole/components/Scrollable) | Renders a sliding window of items (`PageSize`) with built-in keyboard navigation (Arrow keys, PageUp/Down). |

```razor
<Columns Expand>
    <Markup Content="Step 1" />
    <Markup Content="Step 2" />
    <Markup Content="Step 3" />
</Columns>
```

## Input

| Component                                                                       | Highlights                                                                           |
| ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------ |
| [TextButton](https://razorconsole.github.io/RazorConsole/components/TextButton) | Focusable button with customizable colors and click handlers via `EventCallback`.    |
| [TextInput](https://razorconsole.github.io/RazorConsole/components/TextInput)   | Collects user input with placeholder, change handler, and optional password masking. |
| [Select](https://razorconsole.github.io/RazorConsole/components/Select)         | Keyboard-driven dropdown with highlighted selection state and callbacks.             |

```razor
<TextInput Value="@name" ValueChanged="@((v) => name = v)" Placeholder="Enter your name" />
<TextButton Content="Submit" OnClick="HandleSubmit" />
```

## Display

| Component                                                                                     | Highlights                                                                                   |
| --------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| [Markup](https://razorconsole.github.io/RazorConsole/components/Markup)                       | Renders Spectre markup with color, background, and text decoration support.                  |
| [Panel](https://razorconsole.github.io/RazorConsole/components/Panel)                         | Creates framed sections with optional title and border styling.                              |
| [Border](https://razorconsole.github.io/RazorConsole/components/Border)                       | Wraps child content in a configurable border and padding.                                    |
| [BarChart](https://razorconsole.github.io/RazorConsole/components/BarChart)                   | Visualizes numeric data as horizontal bars with customizable styling, labels, and scaling.   |
| [BreakdownChart](https://razorconsole.github.io/RazorConsole/components/BreakdownChart)       | Displays proportional data segments (pie-style) with optional legends and percentage values. |
| [StepChart](https://razorconsole.github.io/RazorConsole/components/StepChart)                 | Plots discrete values over time using Unicode box-drawing characters and multiple series.    |
| [Figlet](https://razorconsole.github.io/RazorConsole/components/Figlet)                       | Produces large ASCII headers using Figlet fonts.                                             |
| [SyntaxHighlighter](https://razorconsole.github.io/RazorConsole/components/SyntaxHighlighter) | Displays colorized source code with optional line numbers.                                   |
| [SpectreCanvas](https://razorconsole.github.io/RazorConsole/components/SpectreCanvas)         | Provides a low-level pixel buffer for drawing custom graphics or pixel art.                  |
| [Markdown](https://razorconsole.github.io/RazorConsole/components/Markdown)                   | Renders Markdown text directly in the console.                                               |
| [Table](https://razorconsole.github.io/RazorConsole/components/Table)                         | Converts semantic `<table>` markup into Spectre tables.                                      |

```razor
<Panel Title="Server Status" BorderColor="Color.Green" Expand>
    <Columns>
        <Markup Content="✓ Online" Color="Color.Green" />
        <Markup Content="Latency: 42ms" Color="Color.Yellow" />
    </Columns>
    <BarChart
        Width="40"
        BarChartItems="@items"
        Label="Load Distribution" />
</Panel>
```

## Utilities

| Component                                                                 | Highlights                                         |
| ------------------------------------------------------------------------- | -------------------------------------------------- |
| [Spinner](https://razorconsole.github.io/RazorConsole/components/Spinner) | Animated progress indicator with optional message. |
| [Newline](https://razorconsole.github.io/RazorConsole/components/Newline) | Emits an empty line to separate sections.          |

```razor
<Spinner Message="Fetching data..." />
```

## Tips

- All components can be composed within one another—wrap inputs inside `Panel`, place buttons in `Columns`, etc.
- Spectre colors map to `Spectre.Console.Color`; use `Color.Red`, `Color.Blue`, or RGB constructors for precise styling.
- Prefer `EventCallback` parameters for handlers so components remain async-friendly.
- Combine with RazorConsole focus management features to deliver intuitive keyboard navigation.
