# RazorConsole Project Overview

## Architecture

```mermaid
flowchart TB
    subgraph RazorComponents["Razor Components"]
        Razor[Razor .razor files]
        Blazor[Blazor Components]
    end

    subgraph Core["RazorConsole.Core"]
        subgraph Rendering["Rendering"]
            Renderer[ConsoleRenderer]
            LiveDisplay[Live display]
            Batch[RenderBatch Processing]
        end

        subgraph VDOM["VDOM"]
            VNode[VNode Tree]
            Diff[VdomDiffService]
        end

        subgraph Layout["Widget layout"]
            Translator[WidgetTranslationContext]
            Custom[Custom IWidgetTranslators]
            Engine[LayoutEngine]
            Canvas[TerminalCanvas]
        end

        subgraph Input["Input"]
            Keyboard[KeyboardEventManager]
            Focus[FocusManager]
        end
    end

    subgraph Spectre["Spectre.Console"]
        ANSI[ANSI Output]
    end

    subgraph Output["Output"]
        Console[Console Application]
        Browser[Browser WASM]
    end

    Razor -->|Component definition| Blazor
    Blazor -->|Render component| Renderer
    Renderer -->|Process RenderBatch| Batch
    Batch -->|Build VNode tree| VNode
    VNode -->|Translate to widgets| Translator
    Custom -->|Run first| Translator
    Translator -->|Widget tree| Engine
    Engine -->|Measure, arrange, paint| Canvas

    Renderer -->|RenderSnapshot| LiveDisplay
    LiveDisplay -->|Diff VNode trees| Diff
    LiveDisplay -->|Write frame| ANSI

    User[User] -->|Keyboard and mouse input| Keyboard
    Keyboard -->|Get focus target| Focus
    Focus -->|Dispatch event| Renderer
    Renderer -->|EventCallback| Blazor
    Blazor -->|State change → re-render| Renderer

    ANSI -->|Native console output| Console
    ANSI -->|WASM interop output| Browser

    Note1[VNode tree:<br/>- Component roots stored separately<br/>- Events captured in descriptors<br/>- Keys for efficient diffing]
    VNode -.-> Note1

    Note2[Every frame:<br/>1. VNode tree<br/>2. Widget tree<br/>3. Layout bounds per node<br/>4. Terminal cells<br/>5. ANSI escape sequences]
    Engine -.-> Note2
```

## Project Structure

The project should contains the following files structure:

.github/
  workflows/ - CI/CD workflows
design-doc/
  project-overview.md - This document
  rendering-process.md - Complete rendering pipeline documentation
  vdom design.md - Virtual DOM architecture
  keyboard-events.md - Keyboard event handling
  wasm-js-interop.md - Browser/WASM interop
  syntax-highlighter.md - Syntax highlighter component design
src/
  RazorConsole.Core - Core library with rendering logic and Razor components
  RazorConsole.Gallery - Sample RazorConsole app
  RazorConsole.Tests - Unit tests for core library

Directory.Build.targets - Centralized build targets
Directory.Build.props - Centralized build properties
Directory.Packages.props - Centralized package versions

global.json - .NET SDK version
README.md - Project overview and instructions
LICENSE - License file
RazorConsole.slnx - Solution file

.gitignore - Git ignore file

## Key Components

### ConsoleRenderer
- Extends Blazor's `Renderer` base class
- Converts `RenderBatch` to `VNode` tree
- Manages component lifecycle
- Handles event dispatching

### VNode Tree
- Immutable virtual DOM representation
- Stores elements, text, components, and regions
- Captures events and attributes
- Enables efficient diffing

### WidgetTranslationContext and LayoutEngine
- Translate the VNode tree into widgets, running custom `IWidgetTranslator`s first
- Measure and arrange widgets, recording the bounds of every node for focus, mouse input and diagnostics
- Paint the widgets into a `TerminalCanvas` of terminal cells
- Collect animated renderables so the display refreshes them

### KeyboardEventManager
- Processes console keyboard input
- Manages text buffers for input elements
- Dispatches events to focused components
- Integrates with FocusManager

### FocusManager
- Tracks focusable elements
- Manages focus traversal (Tab/Shift+Tab)
- Provides focus snapshots for event dispatch
- Handles focus change events
