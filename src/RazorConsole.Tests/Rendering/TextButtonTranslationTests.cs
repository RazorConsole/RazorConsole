// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
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
            await renderer.DispatchEventAsync(current.Root!.Events.Single(e => e.Name == name).HandlerId, args);
            return await notification.Task.WaitAsync(TimeSpan.FromSeconds(5));
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
        // This callback targets the test, not a ComponentBase, so it does not request a render.
        await renderer.DispatchEventAsync(focused.Root!.Events.Single(e => e.Name == "onclick").HandlerId, new MouseEventArgs());
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
}
