# Alternate Screen Buffer

The alternate screen buffer lets a RazorConsole app take over the terminal viewport while it is running, then restore the previous terminal contents when the app exits. It is the same terminal mode used by full-screen console tools such as editors, pagers, and interactive dashboards.

RazorConsole can use this mode for live applications that need a stable screen area. It is especially useful for coordinate-aware interactions because the app's rendered frame starts at the terminal viewport instead of being mixed into scrollback.

## Why it matters

In the normal screen buffer, terminal output is part of scrollback. If the user scrolls, resizes, or launches the app after previous output, terminal coordinates can be harder to relate back to RazorConsole's layout tree. That becomes important for future mouse and pointer interactions, where RazorConsole needs to answer which component owns a specific row and column.

The alternate screen buffer gives the app a clean viewport:

```text
enter alternate screen
  -> render live RazorConsole frame
  -> process keyboard and terminal input
  -> update the latest frame in place
exit alternate screen
  -> restore previous terminal contents
```

This does not add mouse wheel events by itself. Some terminals translate wheel scrolling in alternate screen mode into up and down key input, so components such as `Select` may move when the wheel is scrolled. That behavior comes from the terminal and keyboard event path, not from RazorConsole parsing mouse wheel events.

## Enabling it

Configure the live display options when building the app:

```csharp
builder.UseRazorConsole<App>(configure: config =>
{
    config.Services.Configure<ConsoleAppOptions>(options =>
    {
        options.ConsoleLiveDisplayOptions.UseAlternateScreenBuffer = true;
    });
});
```

When terminal mouse events are enabled, RazorConsole also uses the alternate screen buffer automatically so future coordinate mapping can be based on the rendered viewport:

```csharp
builder.UseRazorConsole<App>(configure: config =>
{
    config.Services.Configure<ConsoleAppOptions>(options =>
    {
        options.ConsoleLiveDisplayOptions.EnableMouseEvents = true;
    });
});
```

## When to use it

Use the alternate screen buffer for interactive, live-updating apps where the terminal viewport acts like the application surface. This includes dashboards, forms, selectors, file explorers, and apps that will eventually depend on coordinate-aware mouse or pointer events.

For short command output, reports, logs, or tools where preserving visible scrollback is more important than owning the viewport, keep the normal screen buffer.

## Exit behavior

RazorConsole restores the normal screen buffer when the live display is disposed. If the process is force-killed or the terminal closes abruptly, the terminal may not receive the restore sequence. In that case, opening a new terminal tab or resetting the terminal usually restores the expected view.
