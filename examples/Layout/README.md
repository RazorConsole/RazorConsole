# Layout Example

Focused sample for investigating RazorConsole layout behavior.

The app uses a body/footer shell. The body renders the selected layout, and the footer uses a wrapping row of `TextButton` controls to switch layouts.

Each layout centers a compact dimension label in its main area so resizing the terminal shows how `Box`, `Flex`, `FillWidth`, and `FillHeight` allocate space.

## Run

WidgetLayout pipeline:

```bash
dotnet run --project examples/Layout/Layout.csproj -f net10.0
```

Resize monitoring is enabled so the WidgetLayout output should re-render as the terminal size changes.