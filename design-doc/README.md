# Design Documentation

This directory contains design documentation for RazorConsole with Mermaid diagrams.

## Viewing Diagrams

The Mermaid diagrams in these documents are automatically rendered by:

- **GitHub**: Diagrams render automatically when viewing `.md` files on GitHub
- **VS Code**: Install the [Markdown Preview Mermaid Support](https://marketplace.visualstudio.com/items?itemName=bierner.markdown-mermaid) extension
- **Other Markdown viewers**: Most modern Markdown viewers support Mermaid natively

## Diagram Types

The documentation uses various Mermaid diagram types:

- **Sequence Diagrams**: Show interaction flows between components
- **Flowcharts**: Display process flows and architecture
- **State Diagrams**: Illustrate component lifecycle and state transitions

## Documentation Files

- `project-overview.md` - Overall architecture and project structure
- `rendering-process.md` - Complete rendering pipeline documentation
- `vdom design.md` - Virtual DOM architecture
- `keyboard-events.md` - Keyboard event handling
- `wasm-js-interop.md` - Browser/WASM interop
- `custom-translators.md` - Custom translator development
- `block-inline-renderable.md` - Block/inline rendering
- `layout-box-flex.md` - Native layout model for Box, Flex, fill, padding, margin, borders, and diagnostics
- `syntax-highlighter.md` - Syntax highlighting component

## Editing Diagrams

To edit Mermaid diagrams:

1. Find the diagram in the markdown file (wrapped in ` ```mermaid ` code blocks)
2. Edit the Mermaid syntax directly
3. Preview changes using GitHub or VS Code with Mermaid support

For Mermaid syntax reference, see: https://mermaid.js.org/
