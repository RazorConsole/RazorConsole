# GalleryMarkup

Standalone example extracted from the RazorConsole Gallery `MarkupGallery.razor` page.

This project keeps the Gallery preview as a runnable sample and intentionally omits the Gallery code-snippet panel.

## Run

Legacy Spectre layout pipeline:

```bash
dotnet run --project examples/GalleryMarkup/GalleryMarkup.csproj -f net10.0
```

WidgetLayout pipeline:

```bash
dotnet run --project examples/GalleryMarkup/GalleryMarkup.csproj -f net10.0 -- --widget-layout
```
