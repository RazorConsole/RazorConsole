# Focus Management

Console UIs need strong focus cues for keyboard navigation. RazorConsole provides a `FocusManager` that automatically tracks focusable elements and coordinates focus changes, making keyboard navigation predictable and accessible.

## Overview

The `FocusManager` is a singleton service that:

- Tracks all focusable elements in the virtual DOM
- Manages focus order and navigation
- Dispatches focus events (`onfocus`, `onfocusin`, `onfocusout`)
- Automatically refocuses when the DOM structure changes
- Provides programmatic control over focus

Focus management is handled automatically when you use RazorConsole's built-in components, but you can also control focus programmatically for advanced scenarios.

## Making Elements Focusable

Elements become focusable in two ways:

### 1. Using `data-focusable` Attribute

Set `data-focusable="true"` on any element to make it focusable:

```razor
<div data-focusable="true" @key="my-element">
    <Markup Content="This element can receive focus" />
</div>
```

### 2. Elements with Event Handlers

Elements with event handlers (like `@onclick`, `@onkeydown`) are automatically focusable:

```razor
<div @onclick="HandleClick" @key="clickable-element">
    <Markup Content="Clickable and focusable" />
</div>
```

### 3. Built-in Components

Built-in components like `TextInput` and `TextButton` are automatically focusable:

```razor
<TextInput Value="@name" ValueChanged="@((v) => name = v)" />
<TextButton Content="Submit" OnClick="HandleSubmit" />
```

## Setting Focus Order

Use the `FocusOrder` parameter on built-in components to control tab order:

```razor
<TextInput Value="@name"
           ValueChanged="@((v) => name = v)"
           FocusOrder="1" />

<TextInput Value="@email"
           ValueChanged="@((v) => email = v)"
           FocusOrder="2" />

<TextButton Content="Submit"
            OnClick="HandleSubmit"
            FocusOrder="3" />
```

For custom elements, use the `data-focus-order` attribute:

```razor
<div data-focusable="true"
     data-focus-order="1"
     @key="first-element">
    <Markup Content="First in tab order" />
</div>

<div data-focusable="true"
     data-focus-order="2"
     @key="second-element">
    <Markup Content="Second in tab order" />
</div>
```

## Keyboard Navigation

Users can navigate between focusable elements using:

- **Tab** - Move focus to the next element
- **Shift + Tab** - Move focus to the previous element

Navigation wraps around: when reaching the last element, Tab moves to the first, and vice versa.

## Programmatic Focus Control

You can programmatically control focus by injecting `FocusManager` into your components.

### Injecting FocusManager

```razor
@using RazorConsole.Core.Focus
@inject FocusManager FocusManager

@code {
    // Use FocusManager methods here
}
```

### Available Methods

#### FocusAsync(string key)

Focus a specific element by its key:

```razor
@using RazorConsole.Core.Focus
@inject FocusManager FocusManager

<TextInput @key="username-input" Value="@username" />
<TextInput @key="password-input" Value="@password" />

<TextButton Content="Focus Username"
            OnClick="FocusUsername" />

@code {
    private string username = string.Empty;
    private string password = string.Empty;

    private async Task FocusUsername()
    {
        await FocusManager.FocusAsync("username-input");
    }
}
```

#### FocusNextAsync() and FocusPreviousAsync()

Move focus programmatically:

```razor
@using RazorConsole.Core.Focus
@inject FocusManager FocusManager

<TextInput Value="@field1" />
<TextInput Value="@field2" />
<TextInput Value="@field3" />

<TextButton Content="Next" OnClick="MoveToNext" />
<TextButton Content="Previous" OnClick="MoveToPrevious" />

@code {
    private string field1 = string.Empty;
    private string field2 = string.Empty;
    private string field3 = string.Empty;

    private async Task MoveToNext()
    {
        await FocusManager.FocusNextAsync();
    }

    private async Task MoveToPrevious()
    {
        await FocusManager.FocusPreviousAsync();
    }
}
```

#### Checking Focus State

Check if an element is currently focused:

```razor
@using RazorConsole.Core.Focus
@inject FocusManager FocusManager

<div @key="my-element"
     data-focusable="true"
     style="@(FocusManager.IsFocused("my-element") ? "highlighted" : "")">
    <Markup Content="This element can be focused" />
</div>

@code {
    // Check current focus
    private string? CurrentFocus => FocusManager.CurrentFocusKey;

    // Check if manager has any focusable elements
    private bool HasFocusables => FocusManager.HasFocusables;
}
```

### Subscribing to Focus Changes

Subscribe to the `FocusChanged` event to react to focus changes:

```razor
@using RazorConsole.Core.Focus
@inject FocusManager FocusManager
@implements IDisposable

<Markup Content="@($"Current focus: {currentFocus}")" />

@code {
    private string? currentFocus;

    protected override void OnInitialized()
    {
        FocusManager.FocusChanged += OnFocusChanged;
        currentFocus = FocusManager.CurrentFocusKey;
    }

    private void OnFocusChanged(object? sender, FocusChangedEventArgs e)
    {
        currentFocus = e.Key;
        StateHasChanged();
    }

    public void Dispose()
    {
        FocusManager.FocusChanged -= OnFocusChanged;
    }
}
```

## Responding to Focus Events

Elements can respond to focus changes using event handlers:

### onfocus

Fired when an element receives focus:

```razor
<div @onfocus="OnFocus"
     data-focusable="true"
     @key="my-element">
    <Markup Content="@message" />
</div>

@code {
    private string message = "Not focused";

    private void OnFocus(FocusEventArgs e)
    {
        message = "Focused!";
        StateHasChanged();
    }
}
```

### onfocusin

Similar to `onfocus`, but bubbles up through parent elements:

```razor
<div @onfocusin="OnFocusIn" data-focusable="true">
    <div data-focusable="true" @key="child">
        <Markup Content="Child element" />
    </div>
</div>

@code {
    private void OnFocusIn(FocusEventArgs e)
    {
        // Fired when child receives focus
    }
}
```

### onfocusout

Fired when an element loses focus:

```razor
<div @onfocus="OnFocus"
     @onfocusout="OnFocusOut"
     data-focusable="true"
     @key="my-element">
    <Markup Content="@message" />
</div>

@code {
    private string message = "Not focused";

    private void OnFocus(FocusEventArgs e)
    {
        message = "Focused!";
        StateHasChanged();
    }

    private void OnFocusOut(FocusEventArgs e)
    {
        message = "Focus lost";
        StateHasChanged();
    }
}
```

## Focus Sessions

Focus management operates within a session that tracks the current render context. Sessions are automatically managed by RazorConsole when your application starts. The `FocusManager`:

- Automatically selects the first focusable element when a session begins
- Maintains focus state across re-renders
- Automatically refocuses when the currently focused element is removed from the DOM
- Clears focus when all focusable elements are removed

## Navigation Between Pages

When navigating between pages or components, focus management automatically adapts:

1. **New Page Loads**: The first focusable element is automatically focused
2. **Component Changes**: Focus is maintained if the same element exists in the new view
3. **Element Removal**: If the focused element is removed, focus moves to the next available element

Example with navigation:

```razor
@using RazorConsole.Core.Focus
@inject FocusManager FocusManager
@inject NavigationManager Navigation

<TextButton Content="Go to Login" OnClick="NavigateToLogin" />

@code {
    private async Task NavigateToLogin()
    {
        Navigation.NavigateTo("/login");
        // Focus will automatically move to the first focusable element
        // on the login page when it renders
    }
}
```

## Complete Example

Here's a complete example demonstrating focus management:

```razor
@using RazorConsole.Core.Focus
@using RazorConsole.Components
@inject FocusManager FocusManager
@implements IDisposable

<Panel Title="Focus Management Demo">
    <Rows>
        <TextInput @key="input1"
                   Value="@value1"
                   ValueChanged="@((v) => value1 = v)"
                   Label="First Input"
                   FocusOrder="1" />

        <TextInput @key="input2"
                   Value="@value2"
                   ValueChanged="@((v) => value2 = v)"
                   Label="Second Input"
                   FocusOrder="2" />

        <TextInput @key="input3"
                   Value="@value3"
                   ValueChanged="@((v) => value3 = v)"
                   Label="Third Input"
                   FocusOrder="3" />

        <Columns>
            <TextButton Content="Focus First" OnClick="FocusFirst" />
            <TextButton Content="Focus Second" OnClick="FocusSecond" />
            <TextButton Content="Focus Third" OnClick="FocusThird" />
            <TextButton Content="Next" OnClick="FocusNext" />
            <TextButton Content="Previous" OnClick="FocusPrevious" />
        </Columns>

        <Markup Content="@($"Current focus: {currentFocus ?? "none"}")"
                Foreground="@Color.Cyan" />
    </Rows>
</Panel>

@code {
    private string value1 = string.Empty;
    private string value2 = string.Empty;
    private string value3 = string.Empty;
    private string? currentFocus;

    protected override void OnInitialized()
    {
        FocusManager.FocusChanged += OnFocusChanged;
        currentFocus = FocusManager.CurrentFocusKey;
    }

    private void OnFocusChanged(object? sender, FocusChangedEventArgs e)
    {
        currentFocus = e.Key;
        StateHasChanged();
    }

    private async Task FocusFirst()
    {
        await FocusManager.FocusAsync("input1");
    }

    private async Task FocusSecond()
    {
        await FocusManager.FocusAsync("input2");
    }

    private async Task FocusThird()
    {
        await FocusManager.FocusAsync("input3");
    }

    private async Task FocusNext()
    {
        await FocusManager.FocusNextAsync();
    }

    private async Task FocusPrevious()
    {
        await FocusManager.FocusPreviousAsync();
    }

    public void Dispose()
    {
        FocusManager.FocusChanged -= OnFocusChanged;
    }
}
```

## Best Practices

1. **Always use `@key` attributes** on focusable elements to ensure stable focus keys
2. **Set `FocusOrder`** on form elements to create a logical tab sequence
3. **Handle focus events** to provide visual feedback when elements receive/lose focus
4. **Use `FocusManager` programmatically** for complex navigation scenarios or when focus needs to change based on application logic
5. **Clean up event subscriptions** by implementing `IDisposable` and unsubscribing from `FocusChanged` events
6. **Test keyboard navigation** to ensure your UI is accessible and intuitive
