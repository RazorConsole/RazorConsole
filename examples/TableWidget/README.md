# TableWidget Example

Minimal sample for exercising the WidgetLayout table path.

It keeps non-table UI to a single `Rows` wrapper and a title, while the tables cover:

- expanded and fixed-width tables
- rounded, square, and ASCII borders
- headers, body rows, and footer rows
- hidden headers
- left, center, and right cell alignment
- explicit header widths
- custom cell padding through attributes
- child widgets inside cells, including `Markup` and `TextButton`

Run with the native widget layout pipeline:

```bash
dotnet run --project examples/TableWidget/TableWidget.csproj -f net10.0 -- --widget-layout
```

Run with the legacy Spectre layout pipeline:

```bash
dotnet run --project examples/TableWidget/TableWidget.csproj -f net10.0
```
