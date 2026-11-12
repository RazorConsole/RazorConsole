# Custom Widget Translators

## Overview

RazorConsole renders a frame in four steps:

```
Razor components -> VDOM -> widgets -> layout -> paint (terminal cells)
```

`WidgetTranslationContext` performs the VDOM-to-widget step. Custom translators (`IWidgetTranslator`) let applications and libraries take part in that step: render their own elements, replace how a built-in element looks, or decorate the default result.

Because a translator always produces a `Widget`, its output is measured, arranged and painted into terminal cells like any built-in widget. It never needs to know where the cells go (a real terminal, the headless test terminal, or diagnostics tooling).

## Contract

```csharp
public delegate Widget WidgetTranslationDelegate(VNode node);

public interface IWidgetTranslator
{
    Widget Translate(WidgetTranslationContext context, WidgetTranslationDelegate next, VNode node);
}
```

- **Chain of responsibility.** Translators run in registration order. Each one either returns a widget for the node or returns `next(node)`. The end of the chain is the built-in translation, which handles every node, so `next` always produces a widget.
- **Every widget node.** `WidgetTranslationContext.Translate` runs the chain for each node it visits, including children that built-in containers translate, so a translator sees custom elements nested anywhere in the tree. Built-in elements that read their content directly into one widget do not visit their inner nodes: inline runs (text, `strong`, `a`, `code`, ... inside a paragraph or inline wrapper), headings, list items and nested lists, the `code` inside a `pre`, and the parts of leaf components such as chart series. A translator changes those by handling the enclosing element.
- **Null results.** A translator must return a widget. `WidgetTranslationContext` throws an `InvalidOperationException` naming the translator when one returns `null`, so the failure points at the culprit instead of surfacing later in layout.
- **Children.** A translator that handles a container translates its children with `context.Translate(child)`, which runs the full chain again. Calling `context.Translate(node)` on the node being handled recurses forever; use `next(node)` instead.
- **Decoration.** Calling `next(node)` and wrapping or replacing the result changes a built-in element without reimplementing it.
- **Identity.** The widget that represents a node should carry the node's `ID`, `Key` and `Attributes`. Layout introspection, focus and mouse hit-testing look widgets up by those values.
- **Animations.** Widgets are recreated on every render, so animated Spectre renderables are registered per translation with `context.TrackAnimation`. The renderer reads `AnimatedRenderables` after translating, and the live display drives all of them from one timer at the shortest `RefreshInterval`, so each animation must derive its frame from the clock rather than from a render count.
- **Spectre content.** `SpectreWidget` wraps any `IRenderable` as a leaf: it measures by rendering at the offered width and paints the resulting segments into cells.

## Registration

```csharp
services.AddWidgetTranslator<MyTranslator>();       // singleton by type
services.AddWidgetTranslator(new MyTranslator());   // existing instance
```

Both register an `IWidgetTranslator` singleton. `WidgetTranslationContext` is itself a singleton that receives `IEnumerable<IWidgetTranslator>` from dependency injection, so the order of the enumerable (registration order) is the order of the chain.

Translators are shared across renders and must not keep per-render state on the instance. Per-render state belongs in the widgets they return.

## Built-in translation

The built-in translation maps RazorConsole's own markup to widgets:

| Markup | Widget |
| --- | --- |
| Text nodes, `span[data-text]` | `TextWidget` |
| `Rows`, `Columns`, `Flex`, `FlexBox` | `FlexWidget` |
| `Panel`, `Padder`, `Box`, `blockquote` | `BoxWidget` |
| `Align` | `AlignWidget` |
| `table`, `Grid` | `TableWidget` |
| `Scrollable`, `ViewHeightScrollable` | `ScrollableWidget` |
| `hr` | `RuleWidget` |
| Paragraphs and inline formatting (`strong`, `em`, `code`, `a`, …) | `SpectreWidget` holding wrapped `Markup` |
| Charts, `SpectreCanvas`, `Spinner`, `SyntaxHighlighter`, `Figlet`, code blocks | `SpectreWidget` leaves |
| Anything else | `StackWidget` of the translated children |

## History

Before 1.0, RazorConsole had a second, Spectre-based pipeline that translated nodes straight to `IRenderable` through `ITranslationMiddleware`. WidgetLayout borrowed that chain as a fallback for some nodes, which made custom middleware run only for those nodes and left their subtrees without layout information. RazorConsole 1.0 removes the legacy pipeline; `IWidgetTranslator` replaces `ITranslationMiddleware` with the same shape but returns widgets and runs for every node that becomes a widget.

## Testing

Translate a VDOM tree directly and lay it out:

```csharp
var context = new WidgetTranslationContext([new MyTranslator()]);
var widget = context.Translate(node);
var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, 40, 0, 10));
```

For an end-to-end check, register the translator in `HeadlessTerminal.StartAsync<TComponent>(width, height, services => services.AddWidgetTranslator<MyTranslator>())` and assert on `CurrentFrame`.
