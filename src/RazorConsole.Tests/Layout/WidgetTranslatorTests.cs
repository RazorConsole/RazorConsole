// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core;
using RazorConsole.Core.Abstractions.Rendering;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Vdom;
using RazorConsole.Testing;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Layout;

public sealed class WidgetTranslatorTests
{
    [Fact]
    public void RegisteredTranslator_HandlesCustomElementsAnywhereInTheTree()
    {
        using var services = new ServiceCollection()
            .AddRazorConsoleServices()
            .AddWidgetTranslator<BadgeTranslator>()
            .BuildServiceProvider();
        var context = services.GetRequiredService<WidgetTranslationContext>();

        var root = VNode.CreateElement("div");
        root.SetAttribute("class", "rows");
        root.AddChild(Badge("ok"));
        root.AddChild(Badge("warn"));

        var widget = context.Translate(root);

        Render(widget, 20).ShouldBe("[ok]\n[warn]");
    }

    [Fact]
    public void Translator_CanDecorateTheBuiltInTranslationThroughNext()
    {
        var context = new WidgetTranslationContext([new FramingTranslator()]);
        var node = VNode.CreateElement("span");
        node.SetAttribute("data-text", "true");
        node.SetAttribute("data-content", "hi");
        node.SetAttribute("data-framed", "true");

        var widget = context.Translate(node);

        widget.ShouldBeOfType<BoxWidget>();
        Render(widget, 10).ShouldBe("╭──╮\n│hi│\n╰──╯");
    }

    [Fact]
    public void Translators_RunInRegistrationOrder()
    {
        var calls = new List<string>();
        var context = new WidgetTranslationContext([new RecordingTranslator("first", calls), new RecordingTranslator("second", calls)]);

        context.Translate(VNode.CreateText("x"));

        calls.ShouldBe(["first", "second"]);
    }

    [Fact]
    public void Translator_ReturningSpectreWidget_PaintsTheRenderable()
    {
        var context = new WidgetTranslationContext([new BadgeTranslator()]);

        var widget = context.Translate(Badge("spectre"));

        widget.ShouldBeOfType<SpectreWidget>();
        Render(widget, 20).ShouldBe("[spectre]");
    }

    [Fact]
    public void TrackAnimation_AddsToAnimatedRenderables()
    {
        var context = new WidgetTranslationContext();
        var spinner = new AnimatedSpinnerRenderable(Spinner.Known.Dots, "busy", null, false);

        context.TrackAnimation(spinner);

        context.AnimatedRenderables.ShouldContain(spinner);
    }

    [Fact]
    public async Task RegisteredTranslator_RendersInARunningApp()
    {
        await using var terminal = await HeadlessTerminal.StartAsync<BadgeHost>(
            40,
            5,
            services => services.AddWidgetTranslator(new BadgeTranslator()),
            TestContext.Current.CancellationToken);

        terminal.CurrentFrame.ContainsText("[from app]").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    [Fact]
    public void Translator_ReturningNull_ThrowsNamingTheTranslator()
    {
        var context = new WidgetTranslationContext([new NullTranslator()]);

        var exception = Should.Throw<InvalidOperationException>(() => context.Translate(VNode.CreateElement("badge")));

        exception.Message.ShouldContain(typeof(NullTranslator).FullName!);
        exception.Message.ShouldContain("<badge>");
    }

    [Fact]
    public void Translator_NullFromNextTranslator_IsReportedForThatTranslator()
    {
        var calls = new List<string>();
        var context = new WidgetTranslationContext([new RecordingTranslator("outer", calls), new NullTranslator()]);

        var exception = Should.Throw<InvalidOperationException>(() => context.Translate(VNode.CreateText("x")));

        exception.Message.ShouldContain(typeof(NullTranslator).FullName!);
        calls.ShouldBe(["outer"]);
    }

    [Fact]
    public void Translator_SeesChildrenOfContainersButNotInlineContentOfParagraphs()
    {
        var seen = new List<string>();
        var context = new WidgetTranslationContext([new TagRecordingTranslator(seen)]);
        var root = VNode.CreateElement("div");
        root.SetAttribute("class", "rows");
        var standalone = VNode.CreateElement("strong");
        standalone.AddChild(VNode.CreateText("alone"));
        root.AddChild(standalone);
        var paragraph = VNode.CreateElement("p");
        paragraph.AddChild(VNode.CreateText("in "));
        var nested = VNode.CreateElement("strong");
        nested.AddChild(VNode.CreateText("paragraph"));
        paragraph.AddChild(nested);
        root.AddChild(paragraph);

        var widget = context.Translate(root);

        // The <strong> inside <p> is part of the paragraph's inline run and is not offered on its own.
        seen.ShouldBe(["div", "strong", "p"]);
        Render(widget, 20).ShouldBe("alone\nin paragraph");
    }

    private static VNode Badge(string label)
    {
        var node = VNode.CreateElement("badge");
        node.SetAttribute("label", label);
        return node;
    }

    private static string Render(Widget widget, int width)
    {
        var result = new LayoutEngine().Layout(widget, new BoxConstraints(0, width, 0, 20));
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });
        var options = new RenderOptions(console.Profile.Capabilities, new Spectre.Console.Size(width, 25));
        var text = string.Concat(result.PaintToRenderable().Render(options, width).Select(segment => segment.IsLineBreak ? "\n" : segment.Text));
        return string.Join("\n", text.Split('\n').Select(line => line.TrimEnd())).TrimEnd('\n');
    }

    private sealed class BadgeTranslator : IWidgetTranslator
    {
        public Widget Translate(WidgetTranslationContext context, WidgetTranslationDelegate next, VNode node)
        {
            if (node.Kind != VNodeKind.Element || node.TagName != "badge")
            {
                return next(node);
            }

            var label = node.Attributes.TryGetValue("label", out var value) ? value : string.Empty;
            return new SpectreWidget(node.ID, new Text($"[{label}]"), node.Key, node.Attributes);
        }
    }

    private sealed class FramingTranslator : IWidgetTranslator
    {
        public Widget Translate(WidgetTranslationContext context, WidgetTranslationDelegate next, VNode node)
        {
            var widget = next(node);
            return node.Attributes.ContainsKey("data-framed")
                ? new BoxWidget(node.ID + "-frame", widget, border: BoxBorderStyle.Rounded)
                : widget;
        }
    }

    private sealed class RecordingTranslator(string name, List<string> calls) : IWidgetTranslator
    {
        public Widget Translate(WidgetTranslationContext context, WidgetTranslationDelegate next, VNode node)
        {
            calls.Add(name);
            return next(node);
        }
    }

    private sealed class NullTranslator : IWidgetTranslator
    {
        public Widget Translate(WidgetTranslationContext context, WidgetTranslationDelegate next, VNode node) => null!;
    }

    private sealed class TagRecordingTranslator(List<string> seen) : IWidgetTranslator
    {
        public Widget Translate(WidgetTranslationContext context, WidgetTranslationDelegate next, VNode node)
        {
            if (node.Kind == VNodeKind.Element)
            {
                seen.Add(node.TagName!);
            }

            return next(node);
        }
    }

    private sealed class BadgeHost : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "badge");
            builder.AddAttribute(1, "label", "from app");
            builder.CloseElement();
        }
    }
}
