### Quick Start Guide

Get up and running with RazorConsole in four straightforward steps.

#### Step 1 · Installation

Install the core package from NuGet:

```shell
dotnet add package RazorConsole.Core
```

#### Step 2 · Project Setup

Enable the Razor SDK so your Razor components compile correctly. Update your project file:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
    <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net8.0</TargetFramework>
        <Nullable>enable</Nullable>
    </PropertyGroup>
</Project>
```

#### Step 3 · Build Your First Component

Create a `Counter.razor` component:

```razor
@using Microsoft.AspNetCore.Components
@using Microsoft.AspNetCore.Components.Web
@using RazorConsole.Components

<Columns>
    <p>Current count</p>
    <Markup Content="@currentCount.ToString()"
            Foreground="@Spectre.Console.Color.Green" />
</Columns>
<TextButton Content="Click me"
            OnClick="IncrementCount"
            BackgroundColor="@Spectre.Console.Color.Grey"
            FocusedColor="@Spectre.Console.Color.Blue" />

@code {
    private int currentCount = 0;

    private void IncrementCount()
    {
        currentCount++;
    }
}
```

Wire it up in `Program.cs`:

```csharp
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<Counter>();

IHost host = hostBuilder.Build();
await host.RunAsync();
```

#### Step 4 · Run the App

Build and run your console UI:

```shell
dotnet run
```

![quick-start-counter image](https://github.com/user-attachments/assets/24d8cc11-6428-4886-93c1-873e45b47c74)

You should see an interactive counter in the terminal. Use **Tab** to traverse focusable elements and **Enter** or **Space** to trigger actions.
