// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;
using RazorConsole.Gallery.Components;
using RazorConsole.Tests.Integration.Infrastructure;

namespace RazorConsole.Tests.Integration;

public sealed class GalleryWorkbenchTests
{
    [Fact]
    public async Task Divider_DragCapture_ClampsWidth_AndSupportsKeyboardAndResize()
    {
        var token = TestContext.Current.CancellationToken;
        await using var terminal = await TestTerminal.StartAsync<App>(100, 35, cancellationToken: token);
        var divider = terminal.GetLayout("gallery-divider");
        var x = divider.Left!.Value;
        var y = divider.Top!.Value + 3;
        var handleY = divider.Top.Value + divider.Height!.Value / 2;
        terminal.Snapshot[x, handleY].Text.ShouldBe("⋮");
        terminal.Snapshot[x, handleY - 1].Text.ShouldBe("│");
        terminal.Snapshot[x, handleY + 1].Text.ShouldBe("│");
        terminal.Snapshot[x, handleY].Style!.Background.ShouldBe(Spectre.Console.Color.Grey70);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, x, y), token);
        terminal.Snapshot[x, y].Style!.Foreground.ShouldBe(Spectre.Console.Color.DeepSkyBlue1);
        terminal.Snapshot[x, handleY].Style!.Background.ShouldBe(Spectre.Console.Color.DeepSkyBlue1);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, 99, 34), token);
        terminal.Snapshot[x, handleY].Style!.Background.ShouldBe(Spectre.Console.Color.Grey70);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, x, y), token);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, x + 8, y), token);
        terminal.GetLayout("gallery-sidebar").Width.ShouldBe(33);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, 99, y), token);
        terminal.GetLayout("gallery-sidebar").Width.ShouldBe(67);
        terminal.GetLayout("gallery-main").Width.ShouldBe(30);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, 0, y), token);
        terminal.GetLayout("gallery-sidebar").Width.ShouldBe(18);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, 80, y), token);
        terminal.GetLayout("gallery-sidebar").Width.ShouldBe(18);
        await terminal.SendKeyAsync(ConsoleKey.RightArrow, cancellationToken: token);
        terminal.GetLayout("gallery-sidebar").Width.ShouldBe(19);
        await terminal.SendKeyAsync(ConsoleKey.Home, cancellationToken: token);
        terminal.GetLayout("gallery-sidebar").Width.ShouldBe(25);
        terminal.Resize(50, 24);
        await terminal.WaitUntilAsync(s => s.Layouts.TryGetValue("gallery-sidebar", out var box) && box.Width == 17, cancellationToken: token);
        terminal.GetLayout("gallery-main").Width.ShouldBe(30);
        terminal.Snapshot.ContainsText("Preview").ShouldBeTrue();
    }

    [Fact]
    public async Task Search_ActivatesOnFocus_AndButtonHoverDoesNotSelect()
    {
        await using var terminal = await TestTerminal.StartAsync<App>(100, 35, cancellationToken: TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("/ Search components").ShouldBeTrue(terminal.DumpDiagnostics());
        await Click(terminal, "gallery-search");
        await terminal.WaitUntilAsync(s => s.ContainsText("Type to search"), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "align-preview");
        await terminal.WaitUntilAsync(s => s.ContainsText("/ Search components"), cancellationToken: TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText(" ● Preview").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.Snapshot.ContainsText(">●").ShouldBeFalse();

        var button = terminal.GetLayout("align-code");
        int x = button.Left!.Value, y = button.Top!.Value;
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, x, y), TestContext.Current.CancellationToken);
        terminal.Snapshot[x, y].Style!.Background.ShouldBe(Spectre.Console.Color.Grey70);
        terminal.CurrentFocusKey.ShouldBe("align-preview");
        terminal.Snapshot.ContainsText("Hello, layout!").ShouldBeTrue();
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, 99, 34), TestContext.Current.CancellationToken);
        (terminal.Snapshot[x, y].Style?.Background ?? Spectre.Console.Color.Default).ShouldBe(Spectre.Console.Color.Default);
    }

    [Fact]
    public async Task Align_MouseControls_UpdatePreviewCodeAndReset()
    {
        await using var terminal = await TestTerminal.StartAsync<App>(100, 35, cancellationToken: TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("Hello, layout!").ShouldBeTrue(terminal.DumpDiagnostics());
        await Click(terminal, "align-h-Right");
        await Click(terminal, "align-wider");
        await Click(terminal, "align-code");
        terminal.Snapshot.ContainsText("HorizontalAlignment.Right").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.Snapshot.ContainsText("32").ShouldBeTrue();
        await Click(terminal, "align-reset");
        terminal.Snapshot.ContainsText("HorizontalAlignment.Center").ShouldBeTrue();
        await Click(terminal, "align-preview");
        terminal.Snapshot.ContainsText("Hello, layout!").ShouldBeTrue();
    }

    [Fact]
    public async Task Search_ClickNavigation_AndResize_WorkTogether()
    {
        await using var terminal = await TestTerminal.StartAsync<App>(100, 35, cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "gallery-search");
        await terminal.SendTerminalInputAsync("text", TestContext.Current.CancellationToken);
        var filtered = await terminal.WaitUntilAsync(s => s.ContainsText("Text Input") && !s.ContainsText("Small Charts"), cancellationToken: TestContext.Current.CancellationToken);
        filtered.ContainsText("Text Input").ShouldBeTrue(terminal.DumpDiagnostics());
        filtered.ContainsText("Small Charts").ShouldBeFalse();
        await Click(terminal, "nav-textinput");
        await terminal.WaitUntilAsync(s => s.ContainsText("TextInput.razor"), cancellationToken: TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("TextInput.razor").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.Resize(80, 24);
        await terminal.WaitUntilAsync(s => s.Width == 80 && s.ContainsText("Ctrl+C quit"), cancellationToken: TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("RazorConsole").ShouldBeTrue();
        terminal.Snapshot.ContainsText("Ctrl+C quit").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    [Fact]
    public async Task NavigationWheel_DoesNotChangeAlignState_KeyboardActivatesTabs()
    {
        await using var terminal = await TestTerminal.StartAsync<App>(100, 30, cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "nav-align");
        await terminal.SendTerminalInputAsync("\u001b[B", TestContext.Current.CancellationToken);
        terminal.CurrentFocusKey.ShouldBe("nav-border");
        await terminal.SendMouseAsync(new(TerminalMouseKind.Wheel, 2, 8, DeltaY: 12), TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("Hello, layout!").ShouldBeTrue();
        await Click(terminal, "align-code");
        await terminal.SendTerminalInputAsync("\r", TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("HorizontalAlignment.Center").ShouldBeTrue();
    }

    [Fact]
    public async Task ContentWheel_ScrollsProperties_WithoutScrollingNavigation()
    {
        await using var terminal = await TestTerminal.StartAsync<App>(100, 20, cancellationToken: TestContext.Current.CancellationToken);
        var navigationTop = terminal.GetLayout("nav-Layout").Top;
        await terminal.SendMouseAsync(new(TerminalMouseKind.Wheel, 40, 10, DeltaY: 12), TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(s => s.ContainsText("Border on"), cancellationToken: TestContext.Current.CancellationToken);
        terminal.GetLayout("nav-Layout").Top.ShouldBe(navigationTop);
        terminal.Snapshot.ContainsText("Ctrl+C quit").ShouldBeTrue();
    }

    private static async Task Click(TestTerminal terminal, string hook)
    {
        var box = terminal.GetLayout(hook);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, box.Left!.Value, box.Top!.Value), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, box.Left!.Value, box.Top!.Value), TestContext.Current.CancellationToken);
    }
}
