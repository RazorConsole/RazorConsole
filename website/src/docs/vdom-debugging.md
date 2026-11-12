### VDom Tree Visualization

Debug and inspect the Virtual DOM structure of your RazorConsole components with the `razorconsole` command-line tool.

#### Overview

RazorConsole turns your Razor components into a Virtual DOM (VDOM), translates it into layout widgets, and paints them into terminal cells. When you debug a complex component hierarchy or a rendering issue, you can read the live tree of a running app, including where each element was laid out.

#### Reading the tree

Start your app in a session and read its tree:

```bash
razorconsole start demo --project ./MyApp
razorconsole tree --depth 3
```

Each line shows an element with its tag, generated id, `@key`, hook, focus key, text, and laid-out position and size (`@left,top widthxheight`). `tree <selector>` limits the output to one element and its children, for example `razorconsole tree hook:sidebar`. Add `--json` for the full tree with each element's attributes.

#### Inspecting one element

```bash
razorconsole find text:Save          # every element that holds "Save"
razorconsole inspect hook:save       # one element and the chain of its ancestors
razorconsole inspect-at 10 4         # the cell at column 10, row 4 and the elements under it
```

Add `data-vnode-hook="<name>"` to an element to give it a stable `hook:<name>` selector. The [command-line tool guide](/docs#cli) describes every selector and command.

#### Watching the tree change

`razorconsole frames` and `razorconsole diff` show recent terminal frames and what changed between them. Run `tree` again after sending keys or clicks to see how the tree changed.

> **Note:** The diagnostics endpoint the tool connects to is enabled by default for Debug builds and the Development environment. Set `ConsoleAppOptions.Diagnostics` to `Disabled` or the `RAZORCONSOLE_DIAGNOSTICS` environment variable to `0` to turn it off.

#### Before 1.0

Earlier versions printed the VDOM tree in each frame when the `RC_PRINT_VDOM_TREE` environment variable was set. That printer was part of the legacy Spectre rendering pipeline, which RazorConsole 1.0 removes, so the variable no longer has any effect.
