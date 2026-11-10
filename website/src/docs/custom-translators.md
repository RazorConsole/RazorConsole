### Custom Translators

RazorConsole turns your Razor components into a virtual DOM, translates every node into a layout **widget**, then measures and paints those widgets into terminal cells. A custom translator lets you decide how particular nodes become widgets, so you can render your own elements or adjust how built-in ones look.

#### Creating a translator

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

Any Spectre.Console `IRenderable` can be wrapped in a `SpectreWidget`. It is laid out as a single leaf and painted into the terminal cells like every other widget.

#### Registering the translator

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
    });

await hostBuilder.Build().RunAsync();
```

Translators run in the order they are added, before RazorConsole's built-in translation, for every node in the tree. `AddWidgetTranslator(instance)` registers an existing instance instead.

#### Using it in components

```razor
<Rows>
    <badge label="Build passed" />
    <badge label="408 tests" />
</Rows>
```

#### Composing widgets and children

A translator can return any widget, not only a `SpectreWidget`. Translate child nodes with `context.Translate(child)`, which runs the whole chain again, and arrange them with the built-in widgets such as `FlexWidget`, `BoxWidget`, `StackWidget` or `TableWidget`.

To change how an existing element looks, call `next(node)` and wrap the result:

```csharp
public sealed class FramedTranslator : IWidgetTranslator
{
    public Widget Translate(WidgetTranslationContext context, WidgetTranslationDelegate next, VNode node)
    {
        var widget = next(node);
        return node.Attributes.ContainsKey("data-framed")
            ? new BoxWidget(node.ID + "-frame", widget, border: BoxBorderStyle.Rounded)
            : widget;
    }
}
```

Keep the node's `ID`, `Key` and `Attributes` on the widget that represents it, so focus, mouse events and layout queries still find the element.

#### Animated content

If a `SpectreWidget` holds a renderable that implements `IAnimatedConsoleRenderable`, call `context.TrackAnimation(renderable)` so the display refreshes it on its interval.

#### Migrating from `ITranslationMiddleware`

RazorConsole 1.0 removes the legacy Spectre rendering pipeline along with `ITranslationMiddleware`, `TranslationContext` and `VdomSpectreTranslator`. The widget translator follows the same chain-of-responsibility shape:

| Before | Now |
| --- | --- |
| `ITranslationMiddleware.Translate(context, next, node)` returning `IRenderable` | `IWidgetTranslator.Translate(context, next, node)` returning `Widget` |
| `return renderable;` | `return new SpectreWidget(node.ID, renderable, node.Key, node.Attributes);` |
| `context.Translate(child)` returning `IRenderable` | `context.Translate(child)` returning `Widget` |
| `context.AnimatedRenderables.Add(animated)` | `context.TrackAnimation(animated)` |
| `services.AddSingleton<ITranslationMiddleware, T>()` | `services.AddWidgetTranslator<T>()` |

Unlike the legacy chain, a widget translator runs for every node, including nodes nested inside built-in components.

For the design behind translation, see the [custom translators design document](https://github.com/RazorConsole/RazorConsole/blob/main/design-doc/custom-translators.md).
