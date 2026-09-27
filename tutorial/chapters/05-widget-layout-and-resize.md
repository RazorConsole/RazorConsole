# Chapter 5 · Widget Layout and Resize

Compose rows, columns, and wrapping content that responds to terminal dimensions.

<!-- interactive-preview -->

## Learning objectives

- choose between `Rows`, `Columns`, and `FlexBox`;
- distinguish fixed cell sizes from expanding content;
- build wrapping layouts; and
- enable native terminal resize monitoring.

## 1. Think in terminal cells

Widget Layout measures width and height in character cells. A `Width="18"` panel occupies 18 columns regardless of
the font's pixel size. Parent widgets measure their children, allocate cells, and then arrange them.

```razor
<Rows>
    <Markup Content="Dashboard" />
    <Columns Expand="true">
        <Panel Title="Build"><Markup Content="Passed" /></Panel>
        <Panel Title="Tests"><Markup Content="408" /></Panel>
    </Columns>
</Rows>
```

Use `Rows` for vertical flow and `Columns` for a small, stable horizontal group. `Expand="true"` lets available width
be shared rather than using only each child's desired width.

## 2. Wrap repeated content

`FlexBox` is useful when the number or width of children can vary:

```razor
<FlexBox Direction="FlexDirection.Row" Wrap="FlexWrap.Wrap" Gap="2">
    @foreach (var card in cards)
    {
        <Panel Width="18" Height="4" Title="@card.Title">
            <Markup Content="@card.Value" />
        </Panel>
    }
</FlexBox>
```

Resize the preview: cards move onto another row when they no longer fit. Keep critical content first because extremely
small terminals may clip the bottom of the composed view.

## 3. Enable resize monitoring

The browser preview forwards xterm resize events automatically. For a native application, enable terminal monitoring:

```csharp
builder.UseRazorConsole<WidgetLayoutAndResize>(configure: app =>
{
    app.Services.Configure<ConsoleAppOptions>(options =>
    {
        options.EnableTerminalResizing = true;
    });
});
```

RazorConsole invalidates layout and renders against the new terminal width and height. Components should express
layout constraints rather than reading pixel dimensions.

## Run this chapter locally

```shell
dotnet run --project tutorial/Tutorial.Runner -- --layout
```

Resize the terminal window and watch the cards wrap and the columns redistribute.

## Exercise

Add a fourth status card and compare `Wrap="FlexWrap.Wrap"` with `FlexWrap.NoWrap` in a narrow terminal.

[← Chapter 4 · Mouse Events](/docs/tutorial/mouse-events) · [Next: Chapter 6 · Routing →](/docs/tutorial/routing)
