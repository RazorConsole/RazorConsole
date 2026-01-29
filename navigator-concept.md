# Navigator Concept for RazorConsole

## Overview

The Navigator component provides stack-based page navigation and modal overlays for RazorConsole applications, enabling multi-screen apps with proper state management and focus restoration.

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                      Navigator                               │
│  ┌─────────────────────────────────────────────────────────┐│
│  │                   Page Stack                            ││
│  │  ┌─────────┐  ┌─────────┐  ┌─────────┐                 ││
│  │  │ Page 1  │→ │ Page 2  │→ │ Page 3  │  (current)      ││
│  │  │(frozen) │  │(frozen) │  │(active) │                 ││
│  │  └─────────┘  └─────────┘  └─────────┘                 ││
│  └─────────────────────────────────────────────────────────┘│
│  ┌─────────────────────────────────────────────────────────┐│
│  │                   Modal Stack                           ││
│  │  ┌─────────┐  ┌─────────┐                              ││
│  │  │ Modal 1 │→ │ Modal 2 │  (topmost, receives focus)   ││
│  │  │(dimmed) │  │(active) │                              ││
│  │  └─────────┘  └─────────┘                              ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

## Key Components

### 1. `Navigator` Component

The central orchestrator that manages both stacks:

```razor
<Navigator InitialPage="typeof(MainMenuPage)">
</Navigator>
```

### 2. `INavigator` Service (injectable)

```csharp
public interface INavigator
{
    // Page navigation (replaces current page, clears forward history)
    Task NavigateToAsync<TPage>(NavigationOptions? options = null) where TPage : IComponent;
    Task NavigateToAsync(Type pageType, NavigationOptions? options = null);
    
    // Stack-based navigation (pushes new page, preserves back stack)
    Task PushAsync<TPage>(NavigationOptions? options = null) where TPage : IComponent;
    Task PopAsync(); // Goes back, destroys current page (fresh on re-push)
    bool CanGoBack { get; }
    
    // Modal management
    Task ShowModalAsync<TModal>(ModalOptions? options = null) where TModal : IComponent;
    Task CloseModalAsync(); // Closes topmost modal
    Task CloseAllModalsAsync();
    bool HasOpenModal { get; }
    
    // Events
    event EventHandler<NavigationEventArgs>? Navigating;
    event EventHandler<NavigationEventArgs>? Navigated;
}
```

### 3. Navigation Options

```csharp
public record NavigationOptions
{
    // Which element to focus when page loads (by key or index)
    public string? InitialFocusKey { get; init; }
    public int? InitialFocusIndex { get; init; }
    
    // Parameters to pass to the page component
    public IDictionary<string, object?>? Parameters { get; init; }
    
    // Animation/transition (future enhancement)
    public NavigationTransition Transition { get; init; } = NavigationTransition.None;
}

public record ModalOptions : NavigationOptions
{
    // Whether clicking outside closes the modal
    public bool CloseOnOutsideClick { get; init; } = true;
    
    // Whether Escape key closes the modal
    public bool CloseOnEscape { get; init; } = true;
    
    // Dim/blur the background
    public bool DimBackground { get; init; } = true;
}
```

## Behavior

### Page Navigation

| Action | Behavior |
|--------|----------|
| `PushAsync<SettingsPage>()` | Current page frozen, SettingsPage created fresh, pushed to stack |
| `PopAsync()` | SettingsPage destroyed, previous page restored with its frozen state & focus |
| `NavigateToAsync<HomePage>()` | Entire stack cleared, HomePage created fresh |

### Modal Behavior

| Action | Behavior |
|--------|----------|
| `ShowModalAsync<ConfirmDialog>()` | Page focus saved, modal rendered on top, modal receives focus |
| `CloseModalAsync()` | Modal destroyed, page focus restored to previous element |
| Escape key (if enabled) | Same as `CloseModalAsync()` |

### Focus Management Integration

```csharp
// When pushing a page:
1. Save current FocusManager.CurrentFocusKey to page state
2. Create new page component
3. Re-render (FocusManager auto-collects new focusable elements)
4. If InitialFocusKey specified, call FocusManager.FocusByKeyAsync()

// When popping:
1. Destroy current page
2. Restore previous page
3. Re-render
4. Restore saved focus key via FocusManager.FocusByKeyAsync()
```

## Example Usage

### Main Menu Page

```razor
@page "/menu"
@inject INavigator Navigator

<Rows>
    <Figlet Content="Main Menu" />
    <TextButton Content="Start Game" OnClick="@(() => Navigator.PushAsync<GamePage>())" />
    <TextButton Content="Settings" OnClick="@(() => Navigator.PushAsync<SettingsPage>())" />
    <TextButton Content="About" OnClick="@ShowAbout" />
    <TextButton Content="Exit" OnClick="@ConfirmExit" />
</Rows>

@code {
    private Task ShowAbout() => Navigator.ShowModalAsync<AboutModal>();
    
    private Task ConfirmExit() => Navigator.ShowModalAsync<ConfirmDialog>(new ModalOptions
    {
        Parameters = new Dictionary<string, object?>
        {
            ["Message"] = "Are you sure you want to exit?",
            ["OnConfirm"] = EventCallback.Factory.Create(this, () => Environment.Exit(0))
        }
    });
}
```

### Settings Page (fresh state on each visit)

```razor
@inject INavigator Navigator

<Rows>
    <Markup Content="Settings" />
    <Select Options="@_themes" @bind-Value="_selectedTheme" />
    <TextButton Content="Back" OnClick="@(() => Navigator.PopAsync())" />
</Rows>
```

### Confirm Dialog (Modal)

```razor
@inject INavigator Navigator

<Panel Title="Confirm" Border="BoxBorder.Double">
    <Rows>
        <Markup Content="@Message" />
        <Columns>
            <TextButton Content="Yes" OnClick="@OnYes" />
            <TextButton Content="No" OnClick="@(() => Navigator.CloseModalAsync())" />
        </Columns>
    </Rows>
</Panel>

@code {
    [Parameter] public string Message { get; set; } = "";
    [Parameter] public EventCallback OnConfirm { get; set; }
    
    private async Task OnYes()
    {
        await OnConfirm.InvokeAsync();
        await Navigator.CloseModalAsync();
    }
}
```

## Implementation Considerations

1. **State Isolation**: Each pushed page is a fresh component instance. Navigator manages component lifecycle explicitly.

2. **Rendering Optimization**: Only active page + modal stack renders. Frozen pages skip re-renders.

3. **Modal Overlay**: Requires new `Overlay`/`Layer` component for rendering on top of existing content.

4. **Keyboard Handling**: Navigator intercepts Escape for modal closing, potentially Back/Backspace for page navigation.

5. **Router Coexistence**: Can work alongside existing Router for hybrid scenarios or replace it for simpler apps.

## Leveraging Blazor's Router & NavigationManager

### What Blazor's Router/NavigationManager Provide

| Feature | How It Works | Fits Navigator Goal? |
|---------|--------------|---------------------|
| **URL-based routing** | `@page "/settings"` → component renders | ✅ Page navigation |
| **NavigationManager.NavigateTo()** | Programmatic navigation | ✅ Basic navigation |
| **LocationChanged event** | Fires when URL changes | ✅ Can hook for focus save/restore |
| **OnNavigateAsync** | Async handler before navigation completes | ✅ Can intercept for state management |
| **RouteData** | Contains matched component type + parameters | ✅ Know which page is active |
| **NotFound content** | Custom content when route not found | ✅ Error handling |

### What's Missing for Navigator Goals

| Goal | Gap |
|------|-----|
| **Fresh state on re-visit** | Router reuses component instances when possible; doesn't guarantee fresh state |
| **Page stack (push/pop)** | No built-in history stack; browser back works but no programmatic stack |
| **Focus save/restore** | Not handled by Router at all |
| **Modal overlay** | No concept of layered rendering |
| **Initial focus specification** | No way to pass "focus this element" to a page |

### Proposed Hybrid Approach

Extend rather than replace the existing infrastructure:

```
┌─────────────────────────────────────────────────────────────┐
│                    Navigator Component                       │
│  ┌─────────────────────────────────────────────────────────┐│
│  │  Wraps Blazor Router + adds:                            ││
│  │  • Page stack management                                ││
│  │  • Focus state per page                                 ││
│  │  • Modal layer                                          ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │  <Router> (standard Blazor)                             ││
│  │    └─ RouteView renders current page                    ││
│  └─────────────────────────────────────────────────────────┘│
│                                                              │
│  ┌─────────────────────────────────────────────────────────┐│
│  │  Modal Layer (rendered on top)                          ││
│  │    └─ Stack of modal components                         ││
│  └─────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────┘
```

### Implementation Strategy

#### 1. Extend ConsoleNavigationManager

```csharp
public class ConsoleNavigationManager : NavigationManager
{
    // Existing...

    // NEW: Page stack for push/pop navigation
    private readonly Stack<PageState> _pageStack = new();

    // NEW: Track focus per page
    public string? CurrentPageFocusKey { get; set; }

    // NEW: Navigation options (initial focus, parameters)
    public NavigationOptions? PendingOptions { get; private set; }

    public void Push(string uri, NavigationOptions? options = null)
    {
        // Save current page state (including focus)
        _pageStack.Push(new PageState(Uri, CurrentPageFocusKey));
        PendingOptions = options;
        NavigateTo(uri);
    }

    public bool Pop()
    {
        if (_pageStack.Count == 0) return false;
        var previous = _pageStack.Pop();
        PendingOptions = new NavigationOptions { InitialFocusKey = previous.FocusKey };
        NavigateTo(previous.Uri);
        return true;
    }
}
```

#### 2. Navigator Component Wraps Router

```razor
@inject ConsoleNavigationManager Nav
@inject FocusManager FocusManager

<div class="navigator-container">
    @* Standard Blazor Router for page content *@
    <Router AppAssembly="@AppAssembly" OnNavigateAsync="OnNavigateAsync">
        <Found Context="routeData">
            <RouteView RouteData="routeData" DefaultLayout="@DefaultLayout" />
        </Found>
        <NotFound>@NotFoundContent</NotFound>
    </Router>

    @* Modal layer rendered on top *@
    @if (_modalStack.Count > 0)
    {
        <div class="modal-overlay">
            @foreach (var modal in _modalStack)
            {
                <DynamicComponent Type="@modal.ComponentType" Parameters="@modal.Parameters" />
            }
        </div>
    }
</div>

@code {
    private async Task OnNavigateAsync(NavigationContext context)
    {
        // Save focus before navigation
        Nav.CurrentPageFocusKey = FocusManager.CurrentFocusKey;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Restore focus after navigation
        if (Nav.PendingOptions?.InitialFocusKey is { } key)
        {
            await FocusManager.FocusByKeyAsync(key, CancellationToken.None);
            Nav.PendingOptions = null;
        }
    }
}
```

#### 3. Fresh State via @key Directive

To ensure fresh component state on each visit, use Blazor's `@key` directive with a changing value:

```razor
@* In Navigator's RouteView wrapper *@
<RouteView @key="@_navigationCounter" RouteData="routeData" />

@code {
    private int _navigationCounter = 0;

    private Task OnNavigateAsync(NavigationContext context)
    {
        _navigationCounter++; // Forces new component instance
        return Task.CompletedTask;
    }
}
```

### What Can Be Reused vs. Built New

| Component | Reuse | Build New |
|-----------|-------|-----------|
| `NavigationManager` | ✅ Extend `ConsoleNavigationManager` | Add stack, focus tracking |
| `Router` | ✅ Use as-is inside Navigator | - |
| `RouteView` | ✅ Use with `@key` for fresh state | - |
| `@page` directives | ✅ Keep existing pattern | - |
| Focus save/restore | - | ✅ Integrate with `FocusManager` |
| Modal stack | - | ✅ New modal layer system |
| `INavigator` service | - | ✅ New high-level API |

### Benefits of This Approach

1. **Backward compatible** - Existing `@page` components and `NavigationManager.NavigateTo()` still work
2. **Familiar patterns** - Developers use standard Blazor routing concepts
3. **Incremental adoption** - Apps can use Navigator features selectively
4. **Leverages Blazor infrastructure** - Route matching, parameters, layouts all work

