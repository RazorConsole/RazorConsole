<div align="center">

<img src="./assets/razor_console logo.svg" alt="RazorConsole Logo" width="128" height="128" />

# RazorConsole

[![NuGet (Stable)](https://img.shields.io/nuget/v/RazorConsole.Core.svg?style=flat-square&logo=nuget&label=stable)](https://www.nuget.org/packages/RazorConsole.Core)
[![NuGet (Nightly)](https://img.shields.io/nuget/vpre/RazorConsole.Core.svg?style=flat-square&logo=nuget&label=nightly&color=orange)](https://www.nuget.org/packages/RazorConsole.Core/absoluteLatest)
[![Component Gallery](https://img.shields.io/nuget/v/RazorConsole.Gallery.svg?style=flat-square&logo=nuget&label=gallery&color=purple)](https://www.nuget.org/packages/RazorConsole.Gallery)
[![NuGet Downloads](https://img.shields.io/nuget/dt/RazorConsole.Core.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/RazorConsole.Core)

[![GitHub Release](https://img.shields.io/github/v/release/RazorConsole/RazorConsole?style=flat-square&logo=github)](https://github.com/RazorConsole/RazorConsole/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0%20%7C%2011.0%20RC-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![codecov](https://img.shields.io/codecov/c/github/RazorConsole/RazorConsole?style=flat-square&logo=codecov&token=)](https://codecov.io/gh/RazorConsole/RazorConsole)
[![Website](https://img.shields.io/badge/docs-razorconsole.com-blue?style=flat-square)](https://razorconsole.com/)

[![Discord](https://img.shields.io/badge/Discord-Join%20Server-5865F2?style=flat-square&logo=discord&logoColor=white)](https://discord.gg/DphHAnJxCM)

**Build rich, interactive console applications using familiar Razor syntax and the power of Spectre.Console**

</div>

## 🎯 What is RazorConsole?

RazorConsole bridges the gap between modern web UI development and console applications. It lets you create sophisticated terminal interfaces using Razor components, complete with interactive elements, rich styling, and familiar development patterns.

## 📦 Install

```bash
dotnet add package RazorConsole.Core
```

## 🚀 Usage

### Project Setup

RazorConsole requires the Microsoft.NET.Sdk.Razor SDK to compile Razor components. Update your project file (`.csproj`) to use the Razor SDK:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
    <!-- other settings -->
</Project>
```

### Basic Example

Here's a simple counter component to get you started:

#### Counter.razor
```razor
@using Microsoft.AspNetCore.Components
@using Microsoft.AspNetCore.Components.Web
@using RazorConsole.Components

<Columns>
    <p>Current count</p>
    <Markup Content="@currentCount.ToString()" Foreground="@Spectre.Console.Color.Green" />
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
#### Program.cs
```csharp
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<Counter>();
IHost host = hostBuilder.Build();
await host.RunAsync();
```

![Image](https://github.com/user-attachments/assets/24d8cc11-6428-4886-93c1-873e45b47c74)

For more complete sample applications, check out the [Examples](#examples) section below.

### Rendering pipeline

RazorConsole renders every frame through WidgetLayout: components become widgets
that are measured, arranged and painted into terminal cells, which gives every
element known bounds for focus, layout queries and coordinate-aware mouse events.
Spectre.Console-backed content such as charts, Figlet text and syntax highlighting
is painted as widget leaves.

The legacy Spectre.Console layout pipeline was removed in 1.0 (earlier warnings
said 1.1.0). If you upgrade from 0.x, remove any `ConsoleAppOptions.RenderingPipeline`
assignment and the `RAZORCONSOLE_RENDERING_PIPELINE` environment variable, which
no longer has any effect, and move custom `ITranslationMiddleware` translators to
`IWidgetTranslator`. `ConsoleAppOptions.AfterRenderAsync` was also removed; use the
root component's `OnAfterRenderAsync`, `IHostApplicationLifetime.ApplicationStarted`
or an `IHostedService` instead. `ConsoleViewResult`, `ConsoleLiveDisplayContext` and
`FocusManager.BeginSession` are now internal. See the
[Widget Layout guide](website/src/docs/widget-layout.md#upgrading-from-0x) and the
[custom translator guide](website/src/docs/custom-translators.md) for details.

Animated applications can opt into synchronized frame presentation with
`options.ConsoleLiveDisplayOptions.EnableSynchronizedUpdates = true`.
On terminals supporting DEC private mode 2026, all changed rows are presented
together instead of showing partially drawn frames. This option is off by default;
terminals without synchronized-output support may still show tearing.

## ✨ Key Features

### 🧩 **Component-Based Architecture**
Build your console UI using familiar Razor components with full support for data binding, event handling, and component lifecycle methods.

### 🎮 **Interactive Components**
Create engaging user experiences with interactive elements like buttons, text inputs, selectors, and keyboard navigation - all with focus management handled automatically.

### 🎯 **Built-in Component Library**
Get started quickly with **25+ pre-built components** covering layout, input, display, and navigation needs:
- **Layout** (6): `Align`, `Columns`, `FlexBox`, `Rows`, `Grid`, `Padder`
- **Input** (3): `TextInput`, `TextButton`, `Select`
- **Display** (12): `BarChart`, `BreakdownChart`, `StepChart`, `Border`, `Figlet`, `Markdown`, `Markup`, `ModalWindow`, `Panel`, `SpectreCanvas`, `SyntaxHighlighter`, `Table`
- **Utilities** (2): `Spinner`, `Newline`


For a full list of components and usage details, see the [Built-in Components](#built-in-components) section below.

### ⚡ **Hot Reload Support**
Experience rapid development with built-in hot reload support. See your UI changes instantly without restarting your application.

### 🧪 **Testing**
Test components headlessly or run your whole app in a real pseudoterminal with the `RazorConsole.Testing` package. See the [Testing guide](https://razorconsole.com/blog/testing/).

### 🖥️ **Command-Line Tool**
Drive and inspect a running app from the command line: start it in a pseudoterminal, press keys and clicks, wait for the screen, and read the component tree, errors and logs of a Development or Debug build. Install `RazorConsole.Cli` as a dotnet tool and run `razorconsole`; results can be JSON with fixed exit codes for CI and AI agents. See the [Command-Line Tool guide](https://razorconsole.com/blog/cli/). An [agent plugin](plugins/razorconsole/README.md) adds RazorConsole skills and the MCP server to Claude Code, VS Code and Copilot, Codex and Antigravity in one install.

### 🎪 **Interactive Component Gallery**
Explore all components hands-on with the included interactive gallery tool. Install globally and run `razorconsole-gallery` to see live examples of every component in action.

For more details, see the [Component Gallery](#component-gallery) section below.


## Built-in components

RazorConsole ships with a catalog of ready-to-use components that wrap Spectre.Console constructs:

- `Align` – position child content horizontally and vertically within a fixed box.
- `BarChart` – Renders a horizontal bar chart with optional label, colors and value display.
- `Border` – draw Spectre borders with customizable style, color, and padding.
- `BreakdownChart` – Renders a colorful breakdown (pie-style) chart showing proportions with optional legend and values.
- `Columns` – arrange items side-by-side, optionally stretching to fill the console width.
- `Figlet` – render big ASCII art text using FIGlet fonts.
- `FlexBox` – lay out child items using a CSS-like flexbox model with configurable direction, justification, alignment, wrapping, and gap.
- `Grid` – build multi-row, multi-column layouts with precise cell control.
- `Markup` – emit styled text with Spectre markup tags.
- `Markdown` - render markdown string.
- `ModalWindow` – display modal overlay windows with automatic centering using z-index positioning.
- `Newline` – insert intentional spacing between renderables.
- `Padder` – add outer padding around child content without altering the child itself.
- `Panel` – frame content inside a titled container with border and padding options.
- `Rows` – stack child content vertically with optional expansion behavior.
- `Scrollable` - enables keyboard-based vertical scrolling through the component's content, including nested components or HTML markup.
- `Select` – present a focusable option list with keyboard navigation.
- `SpectreCanvas` - draws an array of pixels with different colors.
- `Spinner` – show animated progress indicators using Spectre spinner presets.
- `StepChart` – Renders a terminal step chart using Unicode box-drawing characters. Perfect for displaying discrete value changes over time or categories.
- `SyntaxHighlighter` – colorize code snippets using ColorCode themes.
- `Table` – display structured data in formatted tables with headers, borders, and rich cell content.
- `TextButton` – display clickable text with focus and pressed-state styling.
- `TextInput` – capture user input with optional masking and change handlers.

See [`design-doc/builtin-components.md`](design-doc/builtin-components.md) for the full reference, including parameters and customization tips.

## Custom Translators

RazorConsole translates the virtual DOM of your Razor components into layout widgets before measuring and painting them. Custom translators let you render your own elements, or change how built-in ones look, by deciding which widget a node becomes.

### Creating a Custom Translator

Implement `IWidgetTranslator`. Handle the nodes you care about and pass everything else to `next`:

```csharp
using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;
using Spectre.Console;

public sealed class BadgeTranslator : IWidgetTranslator
{
    public Widget Translate(WidgetTranslationContext context, WidgetTranslationDelegate next, VNode node)
    {
        if (node.Kind != VNodeKind.Element || node.TagName != "badge")
        {
            return next(node);
        }

        var label = node.Attributes.TryGetValue("label", out var value) ? value : string.Empty;
        return new SpectreWidget(node.ID, new Markup($"[black on green] {Markup.Escape(label ?? "")} [/]"), node.Key, node.Attributes);
    }
}
```

Any Spectre.Console `IRenderable` can be wrapped in a `SpectreWidget`; it takes part in layout and paints into the terminal cells like every other widget. A translator can also return built-in widgets such as `FlexWidget` or `BoxWidget`, translating children with `context.Translate(child)`.

### Registering a Custom Translator

```csharp
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;

IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<MyComponent>(configure: config =>
        {
            config.ConfigureServices(services =>
            {
                services.AddWidgetTranslator<BadgeTranslator>();
            });
        }
    );

IHost host = hostBuilder.Build();
await host.RunAsync();
```

Translators run in the order they are added, before the built-in translation, for every node that becomes a widget. Inline content inside a paragraph, headings, list items and the parts of built-in components are read directly by their element; see [which nodes reach a translator](website/src/docs/custom-translators.md#which-nodes-reach-a-translator).

### Using Custom Translators in Components

```razor
<Rows>
    <badge label="Build passed" />
    <badge label="408 tests" />
</Rows>
```

### Learn More

See the [custom translator guide](website/src/docs/custom-translators.md), including how to decorate built-in elements and migrate from the 0.x `ITranslationMiddleware` API, and the [design document](design-doc/custom-translators.md).

## Component Gallery

Explore the built-in components interactively with the RazorConsole Component Gallery. The native application does not require the .NET SDK or runtime.

macOS or Linux:

```bash
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s -- --app Gallery
```

Windows PowerShell:

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1))) -App Gallery
```

To test the latest successful Native AOT build from `main`, install the nightly channel:

```bash
curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.sh | sh -s -- --app Gallery --channel nightly
```

```powershell
& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1))) -App Gallery -Channel Nightly
```

After installation, run `razorconsole-gallery` to browse component examples rendered in the console. See the [Component Gallery installation guide](https://razorconsole.com/docs/component-gallery/) for manual downloads, checksum verification, and supported platforms.

![Component Gallery](./assets/gallery.png)

## Examples

Check out the [`examples/`](examples/) folder for complete sample applications that demonstrate RazorConsole in action:

### Counter

A simple counter application that demonstrates the basics of RazorConsole:
- Interactive buttons with keyboard navigation
- State management and event handling
- Layout with Rows, Columns, and Panels
- Styled text with Markup and Figlet components

Perfect for getting started with RazorConsole. See [`examples/Counter/`](examples/Counter/) for the complete implementation.

### Razor Snake

A Native AOT terminal game that demonstrates:
- Real-time keyboard input and a cancellable game loop
- A branded 80×24 interface built from RazorConsole components
- Adaptive WidgetLayout rendering and mouse controls
- A self-contained, zero-JIT native executable

See [`gallery/RazorConsole.Snake/`](gallery/RazorConsole.Snake/) for controls and Native AOT publish commands.

### LLM Agent TUI

A Claude Code-inspired chat interface that demonstrates:
- Integration with Microsoft.Extensions.AI SDK
- Support for multiple LLM providers (OpenAI, Ollama)
- Interactive chat with conversation history
- Real-time message updates and loading states
- Professional TUI design with panels and styled components

See [`examples/LLMAgentTUI/`](examples/LLMAgentTUI/) for the complete implementation and setup instructions.

### Login Form

A CLI login screen that demonstrates form validation and input handling:
- TextInput for username entry with validation
- Password input using TextInput with masking (MaskInput=true)
- Interactive TextButton components for actions
- Input validation with minimum length requirements
- Error highlighting with dynamic border colors
- Real-time validation feedback and error messages
- Login/logout state management

See [`examples/LoginForm/`](examples/LoginForm/) for the complete implementation.

## HotReload

RazorConsole supports hotreload via metadata update handler so you can apply UI changes on the fly.

## Development

### Code Coverage

This project uses [Codecov](https://codecov.io/) for code coverage reporting. Coverage reports are automatically generated during CI builds using Microsoft Testing Platform (MTP) code coverage extension with xUnit.net v3. Coverage data is collected in Cobertura format and uploaded to Codecov for tracking and visualization.

You can view the latest coverage reports at [codecov.io/gh/RazorConsole/RazorConsole](https://codecov.io/gh/RazorConsole/RazorConsole).

### Git LFS

This repository uses [Git LFS](https://git-lfs.github.io/) for tracking large media files. If you're contributing or cloning the repository, make sure you have Git LFS installed:

```bash
# Install Git LFS (if not already installed)
git lfs install

# Clone the repository (LFS files will be downloaded automatically)
git clone https://github.com/RazorConsole/RazorConsole.git
```

The following file types are automatically tracked by Git LFS:
- Images: `*.gif`, `*.png`, `*.jpg`, `*.jpeg`
- Videos: `*.mp4`, `*.mov`, `*.avi`
- Archives: `*.zip`, `*.tar.gz`
- Documents: `*.pdf`

## Showcase

Projects built with RazorConsole:

- [Waves](https://github.com/Skuzzle-UK/Waves) - GitHub Game Off 2025 entry.
- [MandoCode](https://github.com/DevMando/MandoCode) - A CLI coding agent powered by Ollama + Semantic Kernel. Run locally or in the cloud. Refactors code, proposes diffs, and updates your project safely.
- [azure-servicebus-console](https://github.com/zidad/azure-servicebus-console) - Terminal UI browser for Azure Service Bus — browse namespaces, queues, topics and subscriptions, peek/receive messages, and manage entities with delete support

*Want to showcase your project? Submit a PR to add it here!*

## Community & support

- Join our [Discord server](https://discord.gg/DphHAnJxCM) to chat with the community and get help.
- File issues using the GitHub **Issues** tab.
- Read our [Contributing Guidelines](CONTRIBUTING.md) before submitting pull requests.

## License

This project is distributed under the MIT License. See [`LICENSE`](LICENSE) for details.
