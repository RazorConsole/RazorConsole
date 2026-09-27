# Chapter 3 · Text Input and Focus

Collect text, bind values, handle submission, and make keyboard focus visible and predictable.

<!-- interactive-preview -->

## Learning objectives

- bind `TextInput.Value` to component state;
- distinguish input, submit, focus, and blur callbacks;
- navigate focus with the keyboard; and
- mask sensitive display values without losing application state.

## 1. Bind a text input

`@bind-Value` combines the `Value` and `ValueChanged` parameters:

```razor
<TextInput Label="Name"
           @bind-Value="_name"
           Placeholder="Ada"
           FocusedBorderColor="Color.DeepSkyBlue1" />

<Markup Content="@($"Hello, {_name}")" />

@code {
    private string _name = string.Empty;
}
```

The value updates as terminal text input arrives. Use explicit `Value` and `ValueChanged` when the callback needs
validation, normalization, or another side effect.

## 2. Input versus submit

`OnInput` runs for each edit. `OnSubmit` runs when the focused input receives <kbd>Enter</kbd>:

```razor
<TextInput Label="Command" @bind-Value="_command" OnSubmit="RunCommandAsync" />

@code {
    private string _command = string.Empty;

    private Task RunCommandAsync(string? value)
    {
        _command = value ?? string.Empty;
        return Task.CompletedTask;
    }
}
```

## 3. Focus callbacks and traversal

Subscribe with `OnFocus` and `OnBlur`. RazorConsole maintains one focused node, sends keyboard input to it, and uses
the component's focused colors. <kbd>Tab</kbd> moves forward and <kbd>Shift+Tab</kbd> moves backward through focusable
controls.

```razor
<TextInput Label="Name" OnFocus="FocusName" OnBlur="ClearFocus" />
<TextInput Label="Secret" MaskInput="true" OnFocus="FocusSecret" OnBlur="ClearFocus" />
```

`MaskInput` only changes the rendered characters. The bound C# value still contains the original text and must be
handled as sensitive data.

## Run this chapter locally

```shell
dotnet run --project tutorial/Tutorial.Runner -- --text-input
```

Type in the first field, submit it, move to the masked field, and activate **Save profile**.

## Exercise

Add a validation message that appears when the name has fewer than three characters.

[← Chapter 2 · State and Events](/docs/tutorial/state-and-events) · [Next: Chapter 4 · Mouse Events →](/docs/tutorial/mouse-events)
