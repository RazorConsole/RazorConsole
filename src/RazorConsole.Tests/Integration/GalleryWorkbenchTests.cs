// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Input;
using RazorConsole.Gallery.Components;
using RazorConsole.Testing;

namespace RazorConsole.Tests.Integration;

public sealed class GalleryWorkbenchTests
{
    [Fact]
    public async Task Divider_DragCapture_ClampsWidth_AndSupportsKeyboardAndResize()
    {
        var token = TestContext.Current.CancellationToken;
        await using var terminal = await HeadlessTerminal.StartAsync<App>(100, 35, cancellationToken: token);
        var divider = terminal.GetLayout("gallery-divider");
        var x = divider.Left!.Value;
        var y = divider.Top!.Value + 3;
        var handleY = divider.Top.Value + divider.Height!.Value / 2;
        terminal.CurrentFrame[x, handleY].Text.ShouldBe("⋮");
        terminal.CurrentFrame[x, handleY - 1].Text.ShouldBe("│");
        terminal.CurrentFrame[x, handleY + 1].Text.ShouldBe("│");
        terminal.CurrentFrame[x, handleY].Style!.Background.ShouldBe(Spectre.Console.Color.Grey70);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, x, y), token);
        terminal.CurrentFrame[x, y].Style!.Foreground.ShouldBe(Spectre.Console.Color.DeepSkyBlue1);
        terminal.CurrentFrame[x, handleY].Style!.Background.ShouldBe(Spectre.Console.Color.DeepSkyBlue1);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, 99, 34), token);
        terminal.CurrentFrame[x, handleY].Style!.Background.ShouldBe(Spectre.Console.Color.Grey70);
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
        terminal.CurrentFrame.ContainsText("Preview").ShouldBeTrue();
    }

    [Fact]
    public async Task Search_ActivatesOnFocus_AndButtonHoverDoesNotSelect()
    {
        await using var terminal = await HeadlessTerminal.StartAsync<App>(100, 35, cancellationToken: TestContext.Current.CancellationToken);
        terminal.CurrentFrame.ContainsText("/ Search components").ShouldBeTrue(terminal.DumpDiagnostics());
        await Click(terminal, "gallery-search");
        await terminal.WaitUntilAsync(s => s.ContainsText("Type to search"), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "align-preview");
        await terminal.WaitUntilAsync(s => s.ContainsText("/ Search components"), cancellationToken: TestContext.Current.CancellationToken);
        terminal.CurrentFrame.ContainsText(" ● Preview").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.CurrentFrame.ContainsText(">●").ShouldBeFalse();

        var button = terminal.GetLayout("align-code");
        int x = button.Left!.Value, y = button.Top!.Value;
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, x, y), TestContext.Current.CancellationToken);
        terminal.CurrentFrame[x, y].Style!.Background.ShouldBe(Spectre.Console.Color.Grey70);
        terminal.FocusKey.ShouldBe("align-preview");
        terminal.CurrentFrame.ContainsText("Hello, layout!").ShouldBeTrue();
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, 99, 34), TestContext.Current.CancellationToken);
        (terminal.CurrentFrame[x, y].Style?.Background ?? Spectre.Console.Color.Default).ShouldBe(Spectre.Console.Color.Default);
    }

    [Fact]
    public async Task Align_MouseControls_UpdatePreviewCodeAndReset()
    {
        await using var terminal = await HeadlessTerminal.StartAsync<App>(100, 35, cancellationToken: TestContext.Current.CancellationToken);
        terminal.CurrentFrame.ContainsText("Hello, layout!").ShouldBeTrue(terminal.DumpDiagnostics());
        await Click(terminal, "align-h-Right");
        await Click(terminal, "align-wider");
        await Click(terminal, "align-code");
        var code = await terminal.WaitUntilAsync(s => s.ContainsText("HorizontalAlignment.Right"), cancellationToken: TestContext.Current.CancellationToken);
        code.ContainsText("32").ShouldBeTrue(terminal.DumpDiagnostics());
        await Click(terminal, "align-reset");
        await terminal.WaitUntilAsync(s => s.ContainsText("HorizontalAlignment.Center"), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "align-preview");
        await terminal.WaitUntilAsync(s => s.ContainsText("Hello, layout!"), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Search_ClickNavigation_AndResize_WorkTogether()
    {
        await using var terminal = await HeadlessTerminal.StartAsync<App>(100, 35, cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "gallery-search");
        await terminal.SendTerminalInputAsync("text", TestContext.Current.CancellationToken);
        var filtered = await terminal.WaitUntilAsync(s => s.ContainsText("Text Input") && !s.ContainsText("Small Charts"), cancellationToken: TestContext.Current.CancellationToken);
        filtered.ContainsText("Text Input").ShouldBeTrue(terminal.DumpDiagnostics());
        filtered.ContainsText("Small Charts").ShouldBeFalse();
        await Click(terminal, "nav-textinput");
        await terminal.WaitUntilAsync(s => s.ContainsText("TextInput.razor"), cancellationToken: TestContext.Current.CancellationToken);
        terminal.CurrentFrame.ContainsText("TextInput.razor").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.Resize(80, 24);
        await terminal.WaitUntilAsync(s => s.Width == 80 && s.ContainsText("Ctrl+C quit"), cancellationToken: TestContext.Current.CancellationToken);
        terminal.CurrentFrame.ContainsText("RazorConsole").ShouldBeTrue();
        terminal.CurrentFrame.ContainsText("Ctrl+C quit").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    [Fact]
    public async Task NavigationWheel_DoesNotChangeAlignState_KeyboardActivatesTabs()
    {
        await using var terminal = await HeadlessTerminal.StartAsync<App>(100, 30, cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "nav-align");
        await terminal.SendTerminalInputAsync("\u001b[B", TestContext.Current.CancellationToken);
        terminal.FocusKey.ShouldBe("nav-border");
        await terminal.SendMouseAsync(new(TerminalMouseKind.Wheel, 2, 8, DeltaY: 12), TestContext.Current.CancellationToken);
        terminal.CurrentFrame.ContainsText("Hello, layout!").ShouldBeTrue();
        await Click(terminal, "align-code");
        await terminal.SendTerminalInputAsync("\r", TestContext.Current.CancellationToken);
        terminal.CurrentFrame.ContainsText("HorizontalAlignment.Center").ShouldBeTrue();
    }

    [Fact]
    public async Task ContentWheel_ScrollsProperties_WithoutScrollingNavigation()
    {
        await using var terminal = await HeadlessTerminal.StartAsync<App>(100, 20, cancellationToken: TestContext.Current.CancellationToken);
        var navigationTop = terminal.GetLayout("nav-Layout").Top;
        await terminal.SendMouseAsync(new(TerminalMouseKind.Wheel, 40, 10, DeltaY: 12), TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(s => s.ContainsText("Border on"), cancellationToken: TestContext.Current.CancellationToken);
        terminal.GetLayout("nav-Layout").Top.ShouldBe(navigationTop);
        terminal.CurrentFrame.ContainsText("Ctrl+C quit").ShouldBeTrue();
    }

    private static async Task Click(HeadlessTerminal terminal, string hook)
    {
        var box = terminal.GetLayout(hook);
        await terminal.ClickAsync(box.Left!.Value, box.Top!.Value, TestContext.Current.CancellationToken);
    }
}
