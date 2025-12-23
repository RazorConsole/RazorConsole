<div align="center">

<img src="./assets/razor_console logo.svg" alt="RazorConsole Logo" width="128" height="128" />

# RazorConsole

[![NuGet (Stable)](https://img.shields.io/nuget/v/RazorConsole.Core.svg?style=flat-square&logo=nuget&label=stable)](https://www.nuget.org/packages/RazorConsole.Core)
[![NuGet (Nightly)](https://img.shields.io/nuget/vpre/RazorConsole.Core.svg?style=flat-square&logo=nuget&label=nightly&color=orange)](https://www.nuget.org/packages/RazorConsole.Core/absoluteLatest)
[![Component Gallery](https://img.shields.io/nuget/v/RazorConsole.Gallery.svg?style=flat-square&logo=nuget&label=gallery&color=purple)](https://www.nuget.org/packages/RazorConsole.Gallery)
[![NuGet Downloads](https://img.shields.io/nuget/dt/RazorConsole.Core.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/RazorConsole.Core)

[![GitHub Release](https://img.shields.io/github/v/release/RazorConsole/RazorConsole?style=flat-square&logo=github)](https://github.com/RazorConsole/RazorConsole/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg?style=flat-square)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%209.0%20%7C%2010.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![codecov](https://img.shields.io/codecov/c/github/RazorConsole/RazorConsole?style=flat-square&logo=codecov&token=)](https://codecov.io/gh/RazorConsole/RazorConsole)

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

## ✨ Key Features

### 🧩 **Component-Based Architecture**
Build your console UI using familiar Razor components with full support for data binding, event handling, and component lifecycle methods.

### 🎮 **Interactive Components**
Create engaging user experiences with interactive elements like buttons, text inputs, selectors, and keyboard navigation - all with focus management handled automatically.

### 🎯 **Built-in Component Library**
Get started quickly with **20+ pre-built components** covering layout, input, display, and navigation needs:
- **Layout** (6): `Align`, `Columns`, `Rows`, `Grid`, `Padder`, `Scrollable`
- **Input** (3): `TextInput`, `TextButton`, `Select`
- **Display** (11): `BarChart`, `BreakdownChart`, `StepChart`, `Border`, `Figlet`, `Markdown`, `Markup`, `Panel`, `SpectreCanvas`, `SyntaxHighlighter`, `Table`
- **Utilities** (2): `Spinner`, `Newline`


For a full list of components and usage details, see the [Built-in Components](#built-in-components) section below.

### ⚡ **Hot Reload Support**
Experience rapid development with built-in hot reload support. See your UI changes instantly without restarting your application.

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
- `Grid` – build multi-row, multi-column layouts with precise cell control.
- `Markup` – emit styled text with Spectre markup tags.
- `Markdown` - render markdown string.
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

RazorConsole uses a Virtual DOM (VDOM) translation system to convert Razor components into Spectre.Console renderables. You can extend this system by creating custom translators to support additional Spectre.Console features or build entirely custom components.

### Creating a Custom Translator

Implement the `IVdomElementTranslator` interface to create a custom translator:

```csharp
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

public sealed class OverflowElementTranslator : IVdomElementTranslator
{
    // Lower priority values are processed first (1-1000+)
    public int Priority => 85;

    public bool TryTranslate(VNode node, TranslationContext context, out IRenderable? renderable)
    {
        renderable = null;

        // Check if this is a div with overflow attribute
        if (node.Kind != VNodeKind.Element || 
            !string.Equals(node.TagName, "div", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!node.TryGetAttributeValue<string>("data-overflow", out var overflowType))
        {
            return false;
        }

        // Translate child nodes
        if (!VdomSpectreTranslator.TryConvertChildrenToRenderables(
            node.Children, context, out var children))
        {
            return false;
        }

        var content = VdomSpectreTranslator.ComposeChildContent(children);

        // Create renderable with overflow handling
        renderable = overflowType?.ToLowerInvariant() switch
        {
            "ellipsis" => new Padder(content).Overflow(Overflow.Ellipsis),
            "crop" => new Padder(content).Overflow(Overflow.Crop),
            "fold" => new Padder(content).Overflow(Overflow.Fold),
            _ => content
        };

        return true;
    }
}
```

### Registering a Custom Translator

Register your translator in your application's service configuration:

```csharp
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using RazorConsole.Core.Vdom;

IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<MyComponent>(configure: config => 
        {
            config.ConfigureServices(services =>
            { 
                // Register your custom translator
                services.AddVdomTranslator<OverflowElementTranslator>();   
            });
        }
    );

IHost host = hostBuilder.Build();
await host.RunAsync();

```

### Using Custom Translators in Components

Once registered, use your custom translator in Razor components:

```razor
<div data-overflow="ellipsis">
    This text will be truncated with ellipsis if it's too long
</div>
```

### Learn More

For comprehensive documentation on custom translators, including:
- Architecture overview and translation pipeline
- Complete reference of built-in translators and priorities
- Utility methods for common translation tasks
- Best practices and advanced scenarios
- Troubleshooting and testing strategies

See the full guide at [`design-doc/custom-translators.md`](design-doc/custom-translators.md).

## Component Gallery

Explore the built-in components interactively with the RazorConsole Component Gallery. Install the tool globally and launch it from any terminal:

```bash
dotnet tool install --global RazorConsole.Gallery --version 0.0.3-alpha.4657e6
```

After installation, run `razorconsole-gallery` to open the showcase and browse component examples rendered in the console. The gallery includes quick links back to this README for more details.

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

- [Waves](https://github.com/Skuzzle-UK/Waves) - GitHub Game Off 2025 entry

*Want to showcase your project? Submit a PR to add it here!*

## Community & support

- Join our [Discord server](https://discord.gg/DphHAnJxCM) to chat with the community and get help.
- File issues using the GitHub **Issues** tab.
- Read our [Contributing Guidelines](CONTRIBUTING.md) before submitting pull requests.

## License

This project is distributed under the MIT License. See [`LICENSE`](LICENSE) for details.
