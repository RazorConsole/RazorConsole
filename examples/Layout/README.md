# Layout Example

Focused sample for investigating RazorConsole layout behavior.

The first scenario renders a rounded `Box` using `FillWidth` and `FillHeight`, with child content centered by an inner `Flex`.

## Run

WidgetLayout pipeline:

```bash
dotnet run --project examples/Layout/Layout.csproj -f net10.0
```

Resize monitoring is enabled so the WidgetLayout output should re-render as the terminal size changes.