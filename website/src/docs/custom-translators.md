### Custom Translators

RazorConsole converts Razor components into Spectre.Console renderables through a Virtual DOM (VDOM) translation pipeline. You can plug into that pipeline with custom translators to render bespoke elements.

#### Creating a translator

Implement the `IVdomElementTranslator` interface and translate nodes that match your criteria:

```csharp
using RazorConsole.Core.Rendering.Vdom;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

public sealed class OverflowElementTranslator : IVdomElementTranslator
{
    // Lower priority values are processed first (1-1000+)
    public int Priority => 85;

    public bool TryTranslate(
        VNode node,
        TranslationContext context,
        out IRenderable? renderable)
    {
        renderable = null;

        // Check for a div with an overflow attribute
        if (node.Kind != VNodeKind.Element ||
            !string.Equals(node.TagName, "div", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!node.Attributes.TryGetValue("data-overflow", out var overflowType))
        {
            return false;
        }

        if (!VdomSpectreTranslator.TryConvertChildrenToRenderables(
            node.Children, context, out var children))
        {
            return false;
        }

        var content = VdomSpectreTranslator.ComposeChildContent(children);

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

#### Registering the translator

```csharp
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using RazorConsole.Core.Vdom;

IHostBuilder hostBuilder = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<MyComponent>(configure: config =>
        {
            config.ConfigureServices(services =>
            {
                services.AddVdomTranslator<OverflowElementTranslator>();
            });
        }
    );

IHost host = hostBuilder.Build();
await host.RunAsync();
```

#### Using it in components

```razor
<div data-overflow="ellipsis">
    This text will be truncated with ellipsis if it's too long
</div>
```

For a deeper dive, read the [custom translators guide](https://github.com/RazorConsole/RazorConsole/blob/main/design-doc/custom-translators.md).
