# Chapter 6 · Routing and Multi-page Apps

Use Blazor routing to switch terminal pages without rebuilding the application host.

<!-- interactive-preview -->

## Learning objectives

- declare routable Razor components;
- render matched pages with `Router` and `RouteView`;
- navigate from terminal controls; and
- provide a not-found view.

## 1. Create routed pages

A page is a component with an `@page` directive:

```razor
@page "/settings"

<Panel Title="Settings">
    <Markup Content="Notifications: On" />
</Panel>
```

Put each page in its own `.razor` file. Routes must be unique within the assembly scanned by the router.

## 2. Add the router

The root component discovers pages and renders the current match:

```razor
<Router AppAssembly="@typeof(App).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="routeData" />
    </Found>
    <NotFound>
        <Markup Content="Page not found." Foreground="Color.Red" />
    </NotFound>
</Router>
```

`RouteView` creates the selected page component. State local to the previous page is disposed when navigation replaces it.

## 3. Navigate from a component

Inject `NavigationManager` and call `NavigateTo` from a normal callback:

```razor
@inject NavigationManager Navigation

<TextButton Content="Settings" OnClick="OpenSettings" />

@code {
    private void OpenSettings() => Navigation.NavigateTo("/settings");
}
```

The live example includes a missing route so you can see the `NotFound` branch.

## Run this chapter locally

```shell
dotnet run --project tutorial/Tutorial.Runner -- --routing
```

Use the three navigation buttons to switch between Home, Settings, and the missing route.

## Exercise

Add an `/about` page and a fourth navigation button. Give the page a parameterized title component.

[← Chapter 5 · Widget Layout and Resize](/docs/tutorial/widget-layout-and-resize) · [Next: Chapter 7 · Async Work →](/docs/tutorial/async-work)
