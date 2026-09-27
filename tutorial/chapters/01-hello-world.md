# Chapter 1 · Hello World

Build your first RazorConsole application and learn how Razor markup becomes an interactive terminal UI.

<!-- interactive-preview -->

## Learning objectives

By the end of this chapter, you will be able to:

- create a console project that compiles Razor components;
- render a Razor component as terminal character cells;
- handle one small interaction; and
- run the same component in the browser preview and a native terminal.

## How RazorConsole renders

RazorConsole uses Razor's component model, but it does **not** create browser DOM elements. A component
produces a virtual tree that RazorConsole lays out into rows and columns of terminal character cells.
Those cells are then written as ANSI terminal output. Components still provide the useful Razor ideas—markup,
C# state, parameters, and event callbacks—while the renderer targets a terminal instead of HTML.

## Prerequisites

- The [.NET 8 SDK or newer](https://dotnet.microsoft.com/download)
- A terminal with ANSI color support
- An editor that supports C# and Razor files

Check your SDK before continuing:

```shell
dotnet --version
```

## 1. Create the project

```shell
dotnet new console -n HelloRazorConsole --framework net8.0
cd HelloRazorConsole
dotnet add package RazorConsole.Core
```

The package command is for users consuming a published RazorConsole release from NuGet. The live preview
above is built from the current repository checkout and can contain changes that are not in the latest NuGet
package yet. To run this exact checkout instead, use the command in **Run the repository version** below.

## 2. Enable the Razor SDK

Replace `HelloRazorConsole.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="RazorConsole.Core" />
  </ItemGroup>
</Project>
```

`Microsoft.NET.Sdk.Razor` invokes the Razor compiler for `.razor` files. A plain
`Microsoft.NET.Sdk` console project does not generate the component classes used by `Program.cs`.

## 3. Add shared imports

Create `_Imports.razor` in the project root:

```razor
@using Microsoft.AspNetCore.Components
@using Microsoft.AspNetCore.Components.Web
@using RazorConsole.Components
@using Spectre.Console
```

Imports in this file apply to every Razor component beneath it.

## 4. Create the component

Create `HelloWorld.razor`:

```razor
<Box Border="BoxBorder.Rounded" Padding="new Padding(1)">
    <Rows>
        <Markup Content="Hello, RazorConsole!" Foreground="@Color.Cyan1" Decoration="@Decoration.Bold" />
        <Markup Content="@InteractionMessage" Foreground="@Color.Grey70" />
        <TextButton Content="Say hello"
                    OnClick="SayHello"
                    BackgroundColor="@Color.Grey"
                    FocusedColor="@Color.Blue" />
        <Markup Content="Press Enter to activate the button." Foreground="@Color.Grey58" />
    </Rows>
</Box>

@code {
    private int _helloCount;

    private string InteractionMessage => _helloCount == 0
        ? "Try the focused button below."
        : $"Hello again! Button pressed {_helloCount} {(_helloCount == 1 ? "time" : "times")}.";

    private void SayHello()
        => _helloCount++;
}
```

`Rows` stacks terminal widgets vertically. `Markup` emits styled character cells, while `TextButton`
participates in terminal focus and invokes its callback when you press <kbd>Enter</kbd>. The counter is only
here to make the result testable; Chapter 2 will cover state and events in detail.

## 5. Start the host

Replace `Program.cs` with:

```csharp
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

var builder = Host.CreateApplicationBuilder(args);
builder.UseRazorConsole<HelloWorld>();

using var host = builder.Build();
await host.RunAsync();
```

Run the app:

```shell
dotnet run
```

The button starts focused. Press <kbd>Enter</kbd> to activate it and <kbd>Ctrl+C</kbd> to stop the app.

## Complete source and repository run

The complete runnable source for this chapter lives in:

- `tutorial/Tutorial.Components/Chapters/HelloWorld.razor`
- `tutorial/Tutorial.Runner/Program.cs`

To run the exact component used by the browser preview from a RazorConsole checkout:

```shell
dotnet run --project tutorial/Tutorial.Runner
```

This repository uses the preview SDK pinned in `global.json`. Install that SDK (including previews), or use
the published-package steps above with a supported stable SDK.

## Exercise

Add a second `TextButton` named **Reset greeting**. Its callback should set `_helloCount` back to zero.
Then add `FocusedColor="@Color.Yellow"` so the two buttons are easy to distinguish while changing focus.

## Common setup errors

| Symptom | Fix |
| --- | --- |
| `.razor` files are ignored or `HelloWorld` cannot be found | Use `Microsoft.NET.Sdk.Razor` in the project file, then rebuild. |
| `RazorConsole` namespaces cannot be found | Run `dotnet restore` and confirm the `RazorConsole.Core` package or project reference exists. |
| The UI draws with broken borders | Use a UTF-8 terminal and a font that contains box-drawing characters. |
| The button does not respond | Give the terminal focus, then press <kbd>Enter</kbd>. Use <kbd>Tab</kbd> when the exercise adds another button. |
| Checkout build requests another SDK | Install the preview SDK version in the repository's `global.json`; NuGet consumers do not need that checkout SDK. |

[Next: Chapter 2 · State and Events →](/docs/tutorial/state-and-events)
