# Chapter 2 · State and Events

Turn static terminal markup into a component whose output changes in response to user actions.

<!-- interactive-preview -->

## Learning objectives

- store UI state in component fields;
- subscribe to component callbacks;
- derive presentation from state; and
- understand when RazorConsole rerenders.

## 1. Add component state

Razor components keep state in ordinary C# fields and properties:

```razor
<Markup Content="@CountText" Foreground="@CountColor" />

@code {
    private int _count;
    private string CountText => $"Count: {_count}";
    private Color CountColor => _count switch
    {
        > 0 => Color.Green,
        < 0 => Color.Red,
        _ => Color.White,
    };
}
```

The terminal output is a projection of `_count`. Do not manually redraw cells; update state and let RazorConsole render
the new component tree.

## 2. Subscribe to callbacks

`TextButton.OnClick` is an `EventCallback`. It accepts a method group or a lambda:

```razor
<Columns>
    <TextButton Content="-1" OnClick="() => Change(-1)" />
    <TextButton Content="+1" OnClick="() => Change(1)" />
    <TextButton Content="Reset" OnClick="Reset" />
</Columns>

@code {
    private void Change(int amount) => _count += amount;
    private void Reset() => _count = 0;
}
```

After a synchronous or asynchronous event callback completes, the component rerenders automatically. Call
`StateHasChanged` only when state changes outside Razor's normal callback or lifecycle flow.

## 3. Keep state local

Each rendered component instance owns its fields. Restarting the browser preview constructs a new instance, so the
counter returns to zero. Share state through parameters or a registered service only when multiple components truly
need the same lifetime.

## Run this chapter locally

```shell
dotnet run --project tutorial/Tutorial.Runner -- --state-events
```

Use <kbd>Tab</kbd> and <kbd>Shift+Tab</kbd> to move between buttons, then press <kbd>Enter</kbd>.

## Exercise

Add a **+10** button, then disable the decrement action when the count reaches `-5`.

[← Chapter 1 · Hello World](/docs/tutorial/hello-world) · [Next: Chapter 3 · Text Input and Focus →](/docs/tutorial/text-input-and-focus)
