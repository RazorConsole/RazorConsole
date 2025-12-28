# Routing

This document explains how routing works in modern `RazorConsole` and how to use it.

---

## 1. Routing basics

Routing in Blazor maps URLs to _Razor components_.

Key pieces:

- `@page` directive – declares a route for a component.
- `<Router>` / `Routes.razor` – central router component that matches URLs to pages.
- `NavigationManager` – service for programmatic navigation.

---

## 2. The `@page` directive

Any Razor component (`.razor`) becomes routable when it has at least one `@page` directive:

```razor
@page "/"
@page "/home"

<h1>Home</h1>
```

- Each `@page` line defines a route template.
- A component can have multiple routes (e.g., an old and a new URL).
- Routes are relative to the app’s base URL `/`.

### Example from a simple page:

```razor
@page "/welcome"

<h1>Welcome!</h1>

<p>Welcome to Blazor!</p>
```

## 3. Route templates & parameters

### 3.1. Basic parameters

You can define route parameters in the template:

```razor
@page "/todos/{id:int}"

<h3>Todo @Id</h3>

@code {
    [Parameter]
    public int Id { get; set; }
}
```

- `{id:int}` – parameter name is `id`, with an `int` constraint.
- To get parameter from route use `[Parameter]` attribute.

### 3.2. Optional parameters

Optional parameters use `?` syntax:

```razor
@page "/products"
@page "/products/{category?}"

<h3>Products @Category</h3>

@code {
    [Parameter]
    public string? Category { get; set; }
}
```

`/products` and `/products/books` both resolve to the same component.

### 3.3. Multiple parameters

```razor
@page "/orders/{year:int}/{month:int}"

@code {
    [Parameter]
    public int Year { get; set; }
    [Parameter]
    public int Month { get; set; }
}
```

### 3.4. Catch-all parameters

Catch-all parameters capture the rest of the URL segment:

```razor
@page "/files/{*path}"

@code {
    [Parameter]
    public string? Path { get; set; }
}
```

Example URLs:

- `/files/readme.txt`
- `/files/images/logo.png`

## 4. Routing in `RazorConsole`

In the new Blazor Web App template, routing is configured by the Routes component.
Its as simple as in [web blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/routing?view=aspnetcore-10.0)
A typical `Routes.razor`

```razor
@using Microsoft.AspNetCore.Components.Routing
<Router AppAssembly="@typeof(Program).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="routeData" DefaultLayout="@typeof(MainLayout)" />
    </Found>
    <NotFound>
        <LayoutView Layout="@typeof(MainLayout)">
            <p>Sorry, there's nothing at this address.</p>
        </LayoutView>
    </NotFound>
</Router>
```

Key points:

- `AppAssembly` - router scans this assembly to find components with `@page`.
- `<RouteView>` - renders the matched component.
- `DefaultLayout` - layout used when the component doesn’t specify its own.
- `<NotFound>`- content shown when no route matches.

## 5. Layouts and route rendering

Layouts are normal components that wrap pages. Common pattern:

```razor
@layout MainLayout
@page "/counter"

<h1>Counter</h1>
...
```

If no `@layout` is specified:

- The `DefaultLayout` from `RouteView` in `Routes.razor` is used.

## 6. Programmatic navigation with `NavigationManager`

Blazor exposes [`NavigationManager`](https://learn.microsoft.com/en-us/aspnet/core/blazor/fundamentals/routing?view=aspnetcore-10.0) for navigating in code-behind or services.
Inject it:

```razor
@inject NavigationManager Navigation
```

Basic usage:

```cs
Navigation.NavigateTo("/counter");
```

## 7. Unsupported features

Feel free to contribute

- `HeadOutlet` - there is no `head` in cli app.
- [Hash routing](https://developer.mozilla.org/en-US/docs/Glossary/Hash_routing)
- `NavLink`

## 8. Examples

[RazorConsole.Gallery](https://github.com/RazorConsole/RazorConsole/blob/main/src/RazorConsole.Gallery)
