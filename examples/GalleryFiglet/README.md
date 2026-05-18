# GalleryFiglet

Standalone example extracted from the RazorConsole Gallery `FigletGallery.razor` page.

This project keeps the Gallery preview as a runnable sample and intentionally omits the Gallery code-snippet panel.

## Run

Legacy Spectre layout pipeline:

```bash
dotnet run --project examples/GalleryFiglet/GalleryFiglet.csproj -f net10.0
```

WidgetLayout pipeline:

```bash
dotnet run --project examples/GalleryFiglet/GalleryFiglet.csproj -f net10.0 -- --widget-layout
```
