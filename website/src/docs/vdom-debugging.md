### VDom Tree Visualization

Debug and inspect the Virtual DOM structure of your RazorConsole components with the built-in VDom tree printer.

#### Overview

RazorConsole uses a Virtual DOM (VDOM) to efficiently render Razor components to the console. When debugging complex component hierarchies or investigating rendering issues, you can enable the VDom tree printer to visualize the internal structure.

#### How to Enable

Set the `RC_PRINT_VDOM_TREE` environment variable to `true` before running your application:

**On Windows (PowerShell):**

```shell
$env:RC_PRINT_VDOM_TREE="true"
dotnet run
```

**On Linux/macOS:**

```shell
RC_PRINT_VDOM_TREE=true dotnet run
```

**Or inline:**

```shell
RC_PRINT_VDOM_TREE=true dotnet watch run
```

#### What You'll See

When enabled, the tree printer displays each frame's VDOM structure in a panel, showing:

- **Node hierarchy** - Visual tree structure with indentation
- **Node types** - Element, Text, Component, or Region
- **Element details** - Tag names, keys, IDs, and text content
- **Attributes** - All data attributes attached to nodes
- **Events** - Registered event handlers (onclick, onfocus, etc.)

**Example output:**

```
Frame 1
┌─────────────────────────────────────────┐
│ • Element div key=root-key text='...'   │
│    v-id=a3f2 attrs[data-rows=true]      │
│   ├── Element span key=first            │
│   │   text='First Item' v-id=b7c4       │
│   │   attrs[data-focusable=true]        │
│   │   events[onfocus, onfocusin]        │
│   └── Element span key=second           │
│       text='Second Item' v-id=c1d8      │
│       attrs[data-focusable=true]        │
│       events[onfocus, onfocusin]        │
└─────────────────────────────────────────┘
```

#### Frame History

The tree printer accumulates frames as your application runs, allowing you to see how the VDOM evolves over time. Each panel represents a snapshot of the VDOM at a specific render cycle.

> **Note:** This feature has a performance impact and is intended for development and debugging only. Don't enable it in production environments.

#### Technical Details

The VDom tree printer is implemented as an `IVdomElementTranslator` with priority 0 (highest priority). When enabled, it intercepts the translation process and generates a text representation of the entire VDOM tree before passing through to the standard rendering pipeline.

For more information on custom translators, see the [Custom Translators](/docs#custom-translators) guide.
