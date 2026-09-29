# Choosing a .NET terminal UI library

For a C# terminal application, compare the programming model, the interactions you need, and the
way you will distribute the finished app. There is no universal winner between RazorConsole,
direct Spectre.Console, and Terminal.Gui. Prototype a representative screen and test it in your
target terminal.

This comparison checked upstream sources on **29 September 2026 (UTC)**. Competitor claims below are
bounded to **Spectre.Console 0.57.2**, the separate **Spectre.Console.Cli 0.55.0** package, and
**Terminal.Gui v2.5.0**. These versions are comparison references, not RazorConsole dependency
requirements. Recheck newer versions before deciding.

## Compare the authoring models

| Approach | How you build the UI | When to evaluate it |
| --- | --- | --- |
| RazorConsole | Compose Razor components, keep state in C#, and attach event handlers in markup. | Your team wants a familiar web-development/component experience in a terminal tool. |
| Direct Spectre.Console | Assemble styled output, renderables, and prompts through C# APIs. | Tables, status output, and prompted workflows meet your needs without a Razor component layer. |
| Terminal.Gui | Compose views, windows, and built-in widgets around an application lifecycle. | You prefer a terminal view/control model, for inline or full-screen applications. |

Spectre.Console is part of RazorConsole's rendering foundation, not a wholly unrelated,
mutually exclusive replacement. Choosing RazorConsole means choosing a component-oriented
authoring model, not rejecting Spectre.Console.

Sources: [Spectre.Console features](https://github.com/spectreconsole/spectre.console/blob/0.57.2/README.md#L18-L27),
[Terminal.Gui features and application example](https://github.com/tui-cs/Terminal.Gui/blob/v2.5.0/README.md#L14-L65),
and the [RazorConsole interactive tutorial](/docs/tutorial/hello-world/).

## Mouse and keyboard interaction

RazorConsole provides built-in keyboard and mouse events that can be handled directly in Razor.
That keeps state and interaction close to the component. Follow the
[keyboard guide](/blog/keyboard-events/), [focus tutorial](/docs/tutorial/text-input-and-focus/),
and [mouse tutorial](/docs/tutorial/mouse-events/). Native applications must enable mouse reporting;
terminal capabilities, focus, and the rendering configuration still matter.

Input support is **not exclusive to RazorConsole**:

- Spectre.Console has interactive prompts. Its
  [selection prompt processes keyboard input](https://github.com/spectreconsole/spectre.console/blob/0.57.2/src/Spectre.Console/Prompts/SelectionPrompt.cs#L129-L150).
  This is not an output-only library. This source review does not establish its full mouse-support
  surface; do not interpret that research limit as a claim that mouse support is absent.
- Terminal.Gui explicitly supports keyboard and mouse input. Its
  [mouse documentation](https://github.com/tui-cs/Terminal.Gui/blob/v2.5.0/docfx/docs/mouse.md#L22-L38)
  describes parsing, click synthesis, event routing, and command dispatch.

Compare how comfortably you can express the interactions your app needs, not simply whether
a library has an “interactive” checkbox.

## NativeAOT and distribution

NativeAOT avoids JIT warm-up and can produce native applications that need no installed .NET
runtime. It does not guarantee a particular startup time, executable size, or faster performance
than another library. Measure the finished application, including initialization and external work.

| Library or package | Verified scope | What to validate in your app |
| --- | --- | --- |
| RazorConsole | NativeAOT support is documented as experimental; native Gallery applications are distributed for supported OS/architecture combinations. | Platform build tools, runtime identifier, trimming/reflection, routing preservation, third-party packages, and required runtime assets. |
| Spectre.Console 0.57.2 | Declares `IsAotCompatible` for net8.0, net9.0, and net10.0, excluding its netstandard2.0 target from that declaration. | The exact package/target you use and the complete application's dependency graph. |
| Spectre.Console.Cli 0.55.0 | This separate command-line package explicitly sets `IsAotCompatible` and `IsTrimmable` to `false` for its modern .NET targets. | Do not infer the CLI package's support from the rendering package, or vice versa. |
| Terminal.Gui v2.5.0 | Targets .NET 10, declares AOT/trimming compatibility, and includes a NativeAOT smoke application. | Application-specific publish/runtime behavior. Some AOT/trimming warnings are suppressed and the smoke path has dynamic-code/trimming annotations; this is not a universal warning-free guarantee. |

Sources: [RazorConsole Native AOT requirements](/blog/native-aot/),
[Spectre.Console project flags](https://github.com/spectreconsole/spectre.console/blob/0.57.2/src/Spectre.Console/Spectre.Console.csproj#L3-L8),
[Spectre.Console.Cli project flags](https://github.com/spectreconsole/spectre.console.cli/blob/0.55.0/src/Spectre.Console.Cli/Spectre.Console.Cli.csproj#L3-L8),
[Terminal.Gui project flags](https://github.com/tui-cs/Terminal.Gui/blob/v2.5.0/Terminal.Gui/Terminal.Gui.csproj#L17-L35),
[smoke publish settings](https://github.com/tui-cs/Terminal.Gui/blob/v2.5.0/Tests/NativeAotSmoke/NativeAotSmoke.csproj#L1-L8),
and [smoke application annotations](https://github.com/tui-cs/Terminal.Gui/blob/v2.5.0/Tests/NativeAotSmoke/Program.cs#L18-L51).
This comparison inspects source declarations; it does not claim to have executed the competitors'
publish or smoke tests.

For RazorConsole, read the experimental limitations before choosing dependencies. Distribution
may require assets alongside the executable; the Gallery's fonts are an example. A native binary
is specific to its target platform, not one executable for every operating system.

## A practical decision sequence

1. Identify whether you need script-friendly output, a prompted workflow, or a sustained interactive UI.
2. Build the same small task with the programming model that best fits your team. Include keyboard
   navigation, focus, resizing, and mouse interactions you actually need.
3. Publish for your intended deployment model early, especially if NativeAOT is a requirement.
4. Evaluate accessibility, terminal compatibility, maintainability, and measured startup in your
   own application rather than relying on a generic ranking.

If Razor composition fits, run the [interactive tutorial](/docs/tutorial/hello-world/) and browse
[component examples](/components/).
