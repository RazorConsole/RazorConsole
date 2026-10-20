// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Components;
using RazorConsole.Core;
using RazorConsole.Core.Controllers;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace RazorConsole.Tests.Rendering;

public sealed class TextButtonTranslationTests
{
    [Fact]
    public async Task BlazorHtmlRenderer_ProducesNoMarkup()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TextButton>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(TextButton.Content)] = "Invisible in HTML" }));
            return output.ToHtmlString();
        });
        html.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("WidgetLayout")]
    [InlineData("LegacySpectre")]
    public async Task NestedButton_UsesCurrentAsyncCallback_AndIgnoresDisposedBindings(string pipeline)
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        services.Configure<ConsoleAppOptions>(options =>
            options.RenderingPipeline = Enum.Parse<RazorConsoleRenderingPipeline>(pipeline));
        using var provider = services.BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(provider);
        var clicks = 0;
        var host = new ButtonHost
        {
            Click = EventCallback.Factory.Create(this, () => clicks++),
        };
        var initial = await renderer.MountComponentAsync(host, ParameterView.Empty, CancellationToken.None);
        var first = initial.Root!.Children.Single();
        first.Kind.ShouldBe(VNodeKind.Component);
        AssertOutput(initial, "Nested", Color.Blue);
        var oldClick = first.Events.Single(e => e.Name == "onclick");

        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var asyncClick = EventCallback.Factory.Create(this, async () =>
        {
            started.TrySetResult();
            await release.Task;
            clicks += 10;
        });
        await host.UpdateAsync(true, 0, asyncClick);
        var unchanged = renderer.RefreshSnapshot().Root!.Children.Single();
        unchanged.ID.ShouldBe(first.ID);
        unchanged.Component.ShouldBeSameAs(first.Component);

        // Even an event from an earlier snapshot reads the current OnClick parameter.
        var pending = renderer.DispatchEventAsync(oldClick, new MouseEventArgs());
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        pending.IsCompleted.ShouldBeFalse();
        release.SetResult();
        await pending;
        clicks.ShouldBe(10);

        await host.UpdateAsync(true, 0, default);
        renderer.RefreshSnapshot().Root!.Children.Single().Events.ShouldNotContain(e => e.Name == "onclick");
        await renderer.DispatchEventAsync(oldClick, new MouseEventArgs());
        clicks.ShouldBe(10);

        var replacement = EventCallback.Factory.Create(this, () => clicks += 100);
        await host.UpdateAsync(true, 1, replacement);
        var second = renderer.RefreshSnapshot().Root!.Children.Single();
        second.ID.ShouldNotBe(first.ID);
        await renderer.DispatchEventAsync(oldClick, new MouseEventArgs());
        clicks.ShouldBe(10);
        var secondClick = second.Events.Single(e => e.Name == "onclick");
        await renderer.DispatchEventAsync(secondClick, new MouseEventArgs());
        clicks.ShouldBe(110);

        await host.UpdateAsync(false, 1, replacement);
        renderer.RefreshSnapshot().Root!.Children.ShouldBeEmpty();
        await renderer.DispatchEventAsync(secondClick, new MouseEventArgs());
        clicks.ShouldBe(110);
        await host.UpdateAsync(true, 1, replacement);
        var third = renderer.RefreshSnapshot().Root!.Children.Single();
        third.ID.ShouldNotBe(second.ID);
        await renderer.DispatchEventAsync(third.Events.Single(e => e.Name == "onclick"), new MouseEventArgs());
        clicks.ShouldBe(210);
    }

    [Theory]
    [InlineData("WidgetLayout")]
    [InlineData("LegacySpectre")]
    public async Task TypedPropertiesAndEvents_UpdateVisualStateWithoutMarkupAttributes(string pipeline)
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        services.Configure<ConsoleAppOptions>(options =>
            options.RenderingPipeline = Enum.Parse<RazorConsoleRenderingPipeline>(pipeline));
        using var provider = services.BuildServiceProvider();
        using var renderer = TestHelpers.CreateTestRenderer(provider);
        var clicks = 0;
        var initial = await renderer.MountComponentAsync<TextButton>(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(TextButton.Content)] = "[red]literal[/]",
            [nameof(TextButton.BackgroundColor)] = Color.Blue,
            [nameof(TextButton.HoverColor)] = Color.Green,
            [nameof(TextButton.FocusedColor)] = Color.Yellow,
            [nameof(TextButton.OnClick)] = EventCallback.Factory.Create(this, () => clicks++),
        }), CancellationToken.None);

        var root = initial.Root!;
        var button = root.Component.ShouldBeOfType<TextButton>();
        root.Kind.ShouldBe(VNodeKind.Component);
        ConsoleViewResult.FromSnapshot(initial).Html.ShouldBeEmpty();
        root.Children.ShouldBeEmpty();
        root.Attributes.Keys.ShouldNotContain("data-style");
        AssertOutput(initial, "[red]literal[/]", Color.Blue);
        var canvas = Substitute.For<ConsoleLiveDisplayContext.ILiveDisplayCanvas>();
        using var display = ConsoleLiveDisplayContext.CreateForTesting(canvas, ConsoleViewResult.FromSnapshot(initial), null);

        async Task<ConsoleRenderer.RenderSnapshot> Send(string name, EventArgs args)
        {
            var current = renderer.RefreshSnapshot();
            var notification = new TaskCompletionSource<ConsoleRenderer.RenderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
            var observer = Substitute.For<IObserver<ConsoleRenderer.RenderSnapshot>>();
            observer.When(o => o.OnNext(Arg.Any<ConsoleRenderer.RenderSnapshot>())).Do(call =>
            {
                var snapshot = call.Arg<ConsoleRenderer.RenderSnapshot>();
                if (snapshot.RenderRevision > current.RenderRevision)
                {
                    notification.TrySetResult(snapshot);
                }
            });
            using var subscription = renderer.Subscribe(observer);
            await renderer.DispatchEventAsync(current.Root!.Events.Single(e => e.Name == name), args);
            return await notification.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        var hovered = await Send("onmouseenter", new MouseEventArgs());
        AssertOutput(hovered, "[red]literal[/]", Color.Green);
        new VdomDiffService().Diff(initial.Root, hovered.Root).HasChanges.ShouldBeFalse();
        hovered.RenderRevision.ShouldBeGreaterThan(initial.RenderRevision);
        display.UpdateView(ConsoleViewResult.FromSnapshot(hovered)).ShouldBeTrue();
        canvas.Received().UpdateTarget(hovered.Renderable);
        display.UpdateView(ConsoleViewResult.FromSnapshot(renderer.RefreshSnapshot())).ShouldBeFalse();
        // An earlier snapshot retains its visual values even though Component is a live reference.
        AssertOutput(initial, "[red]literal[/]", Color.Blue);

        var focused = await Send("onfocus", new FocusEventArgs());
        AssertOutput(focused, "[red]literal[/]", Color.Yellow);
        await renderer.DispatchEventAsync(focused.Root!.Events.Single(e => e.Name == "onclick"), new MouseEventArgs());
        clicks.ShouldBe(1);
        AssertOutput(await Send("onfocusout", new FocusEventArgs()), "[red]literal[/]", Color.Green);
        AssertOutput(await Send("onmouseleave", new MouseEventArgs()), "[red]literal[/]", Color.Blue);

        var before = renderer.RefreshSnapshot();
        await renderer.Dispatcher.InvokeAsync(() => button.SetParametersAsync(ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(TextButton.Content)] = "Updated",
            [nameof(TextButton.BackgroundColor)] = Color.Red,
            [nameof(TextButton.OnClick)] = default(EventCallback),
        })));
        var updated = renderer.RefreshSnapshot();
        AssertOutput(updated, "Updated", Color.Red);
        new VdomDiffService().Diff(before.Root, updated.Root).HasChanges.ShouldBeFalse();
        updated.RenderRevision.ShouldBeGreaterThan(before.RenderRevision);
        display.UpdateView(ConsoleViewResult.FromSnapshot(updated)).ShouldBeTrue();
        canvas.Received().UpdateTarget(updated.Renderable);
        new VdomDiffService().Diff(updated.Root, renderer.RefreshSnapshot().Root).HasChanges.ShouldBeFalse();
        AssertOutput(await Send("onmouseenter", new MouseEventArgs()), "Updated", Color.Red);
    }

    private static void AssertOutput(ConsoleRenderer.RenderSnapshot snapshot, string text, Color background)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings { Out = new AnsiConsoleOutput(new StringWriter()) });
        var options = new RenderOptions(console.Profile.Capabilities, new Size(80, 25));
        var segments = snapshot.Renderable!.Render(options, 80).Where(s => !s.IsControlCode).ToArray();
        string.Concat(segments.Select(s => s.Text)).TrimEnd().ShouldBe(text);
        segments.Where(s => !string.IsNullOrWhiteSpace(s.Text)).ShouldAllBe(s => s.Style.Background == background);
    }

    private sealed class ButtonHost : ComponentBase
    {
        public EventCallback Click { get; set; }
        private bool _show = true;
        private int _key;

        public Task UpdateAsync(bool show, int key, EventCallback click)
            => InvokeAsync(() =>
            {
                _show = show;
                _key = key;
                Click = click;
                StateHasChanged();
            });

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            if (_show)
            {
                builder.OpenComponent<TextButton>(1);
                builder.SetKey(_key);
                builder.AddAttribute(2, nameof(TextButton.Content), "Nested");
                builder.AddAttribute(3, nameof(TextButton.BackgroundColor), Color.Blue);
                builder.AddAttribute(4, nameof(TextButton.OnClick), Click);
                builder.CloseComponent();
            }
            builder.CloseElement();
        }
    }
}
