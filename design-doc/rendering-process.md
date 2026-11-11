# Rendering Process

This document describes the rendering pipeline in RazorConsole, from Razor component to console output.

## Overview

RazorConsole converts Razor components into console output in these stages:

1. **Component Rendering**: Blazor renders Razor components into a `RenderBatch`.
2. **VDOM Construction**: `ConsoleRenderer` applies the batch to a `VNode` tree.
3. **Widget Translation**: `WidgetTranslationContext` translates the `VNode` tree into a `Widget` tree, running registered `IWidgetTranslator`s before the built-in translation.
4. **Layout**: `LayoutEngine` measures and arranges the widgets within the terminal viewport and records the bounds of every node.
5. **Paint**: the laid-out widgets paint into a `TerminalCanvas` of terminal cells; absolute-positioned widgets paint last, in `z-index` order.
6. **Console Output**: the live display writes the frame through Spectre.Console as ANSI escape sequences.

Before 1.0 there was also a legacy pipeline that translated `VNode`s straight to Spectre.Console `IRenderable`s through `ITranslationMiddleware`. It could not report element bounds and was removed in 1.0.

## Complete Rendering Flow

```mermaid
sequenceDiagram
    participant Component as Razor Component
    participant Renderer as ConsoleRenderer
    participant VNode as VNode Tree
    participant Translator as WidgetTranslationContext
    participant Layout as LayoutEngine
    participant Canvas as TerminalCanvas
    participant LiveDisplay as Live display
    participant Output as Console Output

    Note over Component,Output: Initial render and every re-render
    Component->>Renderer: RenderBatch (UpdateDisplayAsync)
    Renderer->>VNode: Apply component edits
    Renderer->>Translator: Translate(root)
    Translator->>Translator: Custom IWidgetTranslators, then built-in translation
    Translator-->>Renderer: Widget tree
    Renderer->>Layout: Layout(widgets, viewport constraints)
    Layout-->>Renderer: LayoutResult (bounds per node)
    Renderer->>Renderer: Update layout accessors, create RenderSnapshot
    Renderer->>LiveDisplay: OnNext(snapshot)
    LiveDisplay->>LiveDisplay: Diff VNode trees
    alt Tree changed
        LiveDisplay->>Canvas: Paint widgets into cells
        Canvas->>Output: Write changed lines as ANSI
    else No change
        LiveDisplay->>LiveDisplay: Skip redraw
    end
```

## Component Lifecycle

```mermaid
stateDiagram-v2
    [*] --> Mount: Component Mount

    state Mount {
        [*] --> InstantiateComponent
        InstantiateComponent --> AssignComponentId
        AssignComponentId --> RenderRootComponent
    }

    state Render {
        RenderRootComponent --> BuildRenderTree
        BuildRenderTree --> CreateRenderBatch
        CreateRenderBatch --> UpdateDisplayAsync
        UpdateDisplayAsync --> BuildVNodeTree
        BuildVNodeTree --> TranslateToWidgets
        TranslateToWidgets --> LayoutWidgets
        LayoutWidgets --> CreateSnapshot
    }

    state Update {
        CreateSnapshot --> NotifyObservers
        NotifyObservers --> DiffVDOM
        DiffVDOM --> PaintCells
        PaintCells --> UpdateConsole
    }

    state Events {
        UpdateConsole --> WaitForInput
        WaitForInput --> InputEvent
        InputEvent --> DispatchEvent
        DispatchEvent --> StateChange
        StateChange --> Render
    }

    Mount --> Render
    Render --> Update
    Update --> Events
    Events --> Render: State change
```

## VDOM Construction Process

```mermaid
flowchart TD
    subgraph Batch["RenderBatch Processing"]
        RenderBatch[RenderBatch]
        Frames[ReferenceFrames]
        Updates[UpdatedComponents]
        Disposed[DisposedComponents]
    end
    
    subgraph Renderer["ConsoleRenderer"]
        Apply[ApplyComponentEdits]
        Build[BuildSubtree]
        Slot[GetOrCreateComponentSlot]
    end
    
    subgraph VNodeTree["VNode Tree"]
        VNode[(VNode)]
        Element[Element]
        Text[Text]
        Comp[Component]
        Region[Region]
    end
    
    RenderBatch --> Apply
    Apply --> Build
    Build --> Build
    Build -->|RenderTreeFrameType.Element| Element
    Build -->|RenderTreeFrameType.Text| Text
    Build -->|RenderTreeFrameType.Component| Comp
    Build -->|RenderTreeFrameType.Region| Region
    
    Element --> Slot
    Comp --> Slot
    Slot --> VNode
    
    note1[Frame types processed:<br/>- Element: Creates VElementNode<br/>- Text: Creates VTextNode<br/>- Markup: Converts HTML to VNode<br/>- Component: Creates placeholder<br/>- Region: Groups children]
    Build -.-> note1
```

## Widget Translation

```mermaid
flowchart TD
    VNode[VNode] --> Custom{Custom IWidgetTranslator<br/>handles it?}
    Custom -->|Yes| CustomWidget[Widget from the translator]
    Custom -->|"No: next(node)"| BuiltIn[Built-in translation]

    BuiltIn -->|Text, span data-text| Text[TextWidget]
    BuiltIn -->|Rows, Columns, Flex, FlexBox| Flex[FlexWidget]
    BuiltIn -->|Panel, Padder, Box| Box[BoxWidget]
    BuiltIn -->|table, Grid| Table[TableWidget]
    BuiltIn -->|Scrollable| Scroll[ScrollableWidget]
    BuiltIn -->|p, strong, em, code, a| Inline[SpectreWidget with wrapped Markup]
    BuiltIn -->|Charts, Figlet, Spinner, SyntaxHighlighter| Leaf[SpectreWidget leaf]
    BuiltIn -->|Anything else| Stack[StackWidget of children]

    Flex -->|children| VNode
    Box -->|children| VNode
    Table -->|cells| VNode
    Stack -->|children| VNode
```

Translators run for every node, including children of built-in containers, because containers translate their children through the same `WidgetTranslationContext.Translate`. Animated renderables found while translating, such as spinners, are collected so the live display can refresh them on their interval. See [custom-translators.md](custom-translators.md).

## Layout and Paint

`LayoutEngine.Layout` measures the widget tree against the viewport constraints, arranges every widget into a `LayoutRect`, and grows the document to fit absolute-positioned widgets. The resulting `LayoutResult` provides:

- the layout box of every node, which feeds `IVNodeLayoutAccessor`, focus and mouse hit-testing, and the diagnostics endpoint;
- `PaintToRenderable()`, which paints the widgets into a `TerminalCanvas` when the frame is written.

`SpectreWidget` leaves render their Spectre.Console renderable at the width they were given and copy the resulting segments into the canvas cells, so Spectre-backed content takes part in layout like any other widget.

## Update Process

```mermaid
sequenceDiagram
    participant Renderer as ConsoleRenderer
    participant LiveDisplay as ConsoleLiveDisplayContext
    participant DiffService as VdomDiffService
    participant Canvas as LiveDisplayCanvas
    participant Terminal as Terminal

    Renderer->>LiveDisplay: OnNext(snapshot)
    alt First frame
        LiveDisplay->>Canvas: UpdateTarget(frame)
    else Later frame
        LiveDisplay->>DiffService: Diff(previousRoot, currentRoot)
        alt Tree changed
            LiveDisplay->>Canvas: UpdateTarget(frame)
        else No change
            LiveDisplay->>LiveDisplay: Skip redraw
        end
    end
    Canvas->>Terminal: Write the lines that changed
```

A terminal resize refreshes the snapshot through the same path, so the frame is laid out again at the new size. Issue #425 plans to make the painted cell grid the pipeline's official output, with terminal, headless and diagnostics backends consuming the same frames.

## Event Handling and Re-render

```mermaid
sequenceDiagram
    participant User as User Input
    participant KbdMgr as KeyboardEventManager
    participant Focus as FocusManager
    participant Renderer as ConsoleRenderer
    participant Component as Component
    participant Callback as EventCallback

    User->>KbdMgr: HandleKeyAsync(keyInfo)
    KbdMgr->>Focus: GetCurrentFocus()
    Focus->>Focus: Get focus snapshot
    Focus->>KbdMgr: Focus target + handlers

    KbdMgr->>KbdMgr: Process key event
    alt Printable character
        KbdMgr->>KbdMgr: Update text buffer
        KbdMgr->>Renderer: DispatchEventAsync(handlerId, oninput)
    end

    KbdMgr->>Renderer: DispatchEventAsync(handlerId, onkeydown)
    Renderer->>Callback: Invoke event handler
    Callback->>Component: Update state
    Component->>Component: StateHasChanged()
    Component->>Renderer: Trigger re-render

    KbdMgr->>Renderer: DispatchEventAsync(handlerId, onkeyup)

    Renderer->>Renderer: UpdateDisplayAsync()
    Note right of Renderer: Re-render cycle starts
```

