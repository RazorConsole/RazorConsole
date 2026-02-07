# MultipageApp Example

A multi-page console application demonstrating Blazor's Router for navigation between pages. This example shows how to:

![Example presentation](./presentation.gif)

- Use Blazor Router for page navigation
- Navigate programmatically with `NavigationManager`
- Create interactive menus with keyboard navigation
- Implement modal dialogs using z-index positioning
- Build reusable slider components with -/+ buttons
- Handle application lifecycle for graceful exit

## Running the Example

From the repository root:

```bash
dotnet run --project examples/MultipageApp
```

Or from this directory:

```bash
dotnet run
```

## Features Demonstrated

- **Multi-Page Navigation**: Uses Blazor's `<Router>` component with `@page` directives
- **Main Menu**: Three navigation options (New Game, Settings, Exit)
- **Settings Page**: Volume sliders with visual indicators
- **Exit Confirmation Modal**: Overlay dialog using absolute positioning and z-index
- **Graceful Shutdown**: Uses `IHostApplicationLifetime` for proper application exit

## Pages

### Menu Page (`/`)

The main entry point with three interactive buttons:
- **New Game** - Navigates to the game placeholder page
- **Settings** - Navigates to the settings page
- **Exit** - Shows a confirmation modal dialog

### Settings Page (`/settings`)

Demonstrates slider controls for audio settings:
- Master Volume slider (0-100%)
- Music Volume slider (0-100%)
- Back button to return to menu

### New Game Page (`/newgame`)

A placeholder page that would contain game logic in a real application.

## Key Components Used

- `Router` - Blazor routing component for page navigation
- `Figlet` - ASCII art titles
- `Panel` - Bordered containers with titles
- `Rows` - Vertical layout organization
- `Columns` - Horizontal layout
- `TextButton` - Interactive buttons with focus states
- `Slider` - Custom slider component with -/+ buttons
- `Markup` - Styled text with colors
- `Align` - Content alignment
- `Padder` - Padding around content
- `Newline` - Spacing between elements

## Code Structure

- `Components/App.razor` - Router configuration with Found/NotFound handling
- `Components/Slider.razor` - Reusable slider component
- `Pages/Menu.razor` - Main menu with exit modal
- `Pages/Settings.razor` - Settings page with volume sliders
- `Pages/NewGame.razor` - Game placeholder page
- `Program.cs` - Application entry point
- `MultipageApp.csproj` - Project configuration

## Implementation Details

### Router Setup

The router is configured in `App.razor`:

```razor
<Router AppAssembly="@typeof(App).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="routeData" />
    </Found>
    <NotFound>
        <Markup Content="Page not found." />
    </NotFound>
</Router>
```

### Programmatic Navigation

Navigation between pages uses `NavigationManager`:

```razor
@inject NavigationManager Nav

<TextButton Content="Settings"
            OnClick="@(() => Nav.NavigateTo("/settings"))" />
```

### Modal Dialog with Z-Index

The exit confirmation modal uses absolute positioning:

```razor
<div position="absolute" top="7" left="0" right="0" z-index="1000">
    <Panel Border="BoxBorder.Double" Title="Confirm Exit">
        <!-- Modal content -->
    </Panel>
</div>
```

### Graceful Application Exit

Uses `IHostApplicationLifetime` to properly stop the application:

```razor
@inject IHostApplicationLifetime AppLifetime

@code {
    private void ExitApp()
    {
        AppLifetime.StopApplication();
    }
}
```
