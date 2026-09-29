# Build a C# terminal UI with Razor components

A terminal user interface (TUI) is useful when a command needs more than a stream of output:
think selectable results, editable fields, dashboards, or a tool that reacts to keyboard and mouse
input. A plain command-line interface may still be the better choice for scripts, pipelines,
and unattended automation. Choose the interface around how people will use the program.

RazorConsole lets you build a .NET TUI by composing Razor components and keeping state and event
handlers in C#. If you know Razor from web development, the component model is familiar; the
output is a terminal rather than a browser DOM. “Ink for .NET” is a useful analogy for that
component-oriented workflow, not a claim that RazorConsole is an official Ink port, a React
implementation, or API-compatible with Ink.

## Start with a working component

Use the [interactive Hello World tutorial](/docs/tutorial/hello-world/) for the current project
setup and a real component you can run in the browser. Continue to
[state and events](/docs/tutorial/state-and-events/) to connect actions to UI updates.
This guide explains the decisions rather than maintaining a second copy of installation commands.

Compose small pieces: a panel for grouping, markup for status text, and input components for user
actions. Explore [built-in components](/components/) and the [API reference](/api/) as you add
parameters and callbacks. Spectre.Console is part of RazorConsole's rendering foundation;
Razor adds the component-oriented authoring experience on top.

## Handle keyboard and mouse input in Razor

Built-in events let you express interaction alongside the component markup. The homepage demo
uses handlers such as `@onkeydown`, `@onmouseenter`, and `@onclick`. Use C# handlers to update
state rather than manually coordinating every terminal redraw.

Follow [text input and focus](/docs/tutorial/text-input-and-focus/) before adding shortcuts:
focus determines where input goes. The [keyboard events guide](/blog/keyboard-events/) covers
the event model; the [mouse tutorial](/docs/tutorial/mouse-events/) covers the required setup,
hover, clicks, wheel input, and dragging. In a native host, enable
`ConsoleLiveDisplayOptions.EnableMouseEvents`; the browser tutorial already enables input.
Mouse mode uses the alternate screen buffer. Terminal capabilities and configuration matter.
Test in the terminal and platform you intend to support, and keep a keyboard path for essential
actions rather than assuming every environment has a usable mouse.

Built-in input is a convenience of RazorConsole, not a claim that other TUI libraries cannot
handle these events.

## Design for terminal dimensions

Terminal layouts work in cells, not browser pixels. Keep information readable when the terminal
shrinks and consider scrolling and focus visibility. Try the
[layout and resize chapter](/docs/tutorial/widget-layout-and-resize/) and check the rendering
pipeline's documented limitations before relying on a specific layout feature.

For a multi-screen tool, continue with [routing](/docs/tutorial/routing/).
For background work, use the [async chapter](/docs/tutorial/async-work/) so that progress and
interaction remain understandable. The [complete app](/docs/tutorial/complete-app/) brings those
pieces together.

## Plan NativeAOT distribution early

NativeAOT can produce native applications that do not require an installed .NET runtime and avoid
JIT warm-up at startup. That can simplify distribution, but does not guarantee an instantaneous
start or a particular file size. Measure your own app if startup or memory is a requirement.

RazorConsole's NativeAOT support is **experimental**. Platform-specific build tools, trimming,
reflection, routing preservation, and third-party packages need attention. Native publishing is
not a promise of one binary for every OS: build for the intended runtime identifier and include
required assets, such as the Gallery's fonts. See the
[Native AOT guide](/blog/native-aot/) before choosing dependencies.

## Decide whether this model fits

RazorConsole is a good starting point when reusable Razor components, state-driven updates, and
direct input handlers fit your team's C# workflow. If your application mainly prints structured
output, direct Spectre.Console may be enough. If you want a different view/control model for a
full-screen terminal application, evaluate Terminal.Gui as well.

Use the [fair .NET TUI selection guide](/guides/choosing-dotnet-tui/) to compare the programming
models and deployment checks, then prototype one representative screen before committing to a library.
