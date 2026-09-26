// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering;
using RazorConsole.Gallery.Components;
using RazorConsole.Tests.Integration.Infrastructure;

namespace RazorConsole.Tests.Integration;

public sealed class GallerySampleTests
{
    [Fact]
    public async Task Drag_CapturesOutsideCard_Clamps_AndSupportsKeyboardAndReset()
    {
        await using var terminal = await Start("drag");
        var card = terminal.GetLayout("drag-card");
        var x = card.Left!.Value + 2;
        var y = card.Top!.Value + 1;
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, x, y), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, x + 5, y + 2), TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(s => s.ContainsText("Position: 7, 3"), cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, 99, 39), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, 99, 39), TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(s => s.ContainsText("Position: 28, 7 · Ready"), cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, 0, 0), TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("Position: 28, 7 · Ready").ShouldBeTrue();
        await terminal.SendTerminalInputAsync("\u001b[D\u001b[A", TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(s => s.ContainsText("Position: 27, 6"), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "drag-reset");
        await terminal.WaitUntilAsync(s => s.ContainsText("Position: 2, 1 · Ready"), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SyntaxBlocks_StackVertically_AndTableBorderHasContrast()
    {
        await using var syntax = await Start("syntax", 100, 120);
        var lines = syntax.Snapshot.ScreenText.Split('\n');
        var previous = -1;
        foreach (var title in new[] { "C#", "TypeScript", "Python", "SQL", "JSON", "Razor" })
        {
            var row = Array.FindIndex(lines, line => line.Length > 27 && line[27..].Trim() == title);
            row.ShouldBeGreaterThan(previous, syntax.DumpDiagnostics());
            previous = row;
        }

        await using var table = await Start("table");
        var corners = new List<Spectre.Console.Color>();
        for (var y = 0; y < table.Snapshot.Height; y++)
        {
            for (var x = 27; x < table.Snapshot.Width; x++)
            {
                var cell = table.Snapshot[x, y];
                if (cell.Text == "╭")
                {
                    corners.Add(cell.Style?.Foreground ?? Spectre.Console.Color.Default);
                }
            }
        }
        corners.ShouldNotBeEmpty(table.DumpDiagnostics());
        corners.ShouldAllBe(color => color == Spectre.Console.Color.Grey58);
    }

    [Fact]
    public async Task WheelBurst_WithRealDiffOutput_ResizeAndInput_RemainsResponsive()
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        // Four frameworks run concurrently with coverage on CI. This is a
        // deadlock watchdog, not a benchmark of the shared runner's throughput.
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        var navigation = new ConsoleNavigationManager();
        navigation.NavigateTo("scrollable");
        await using var terminal = await TestTerminal.StartAsync<App>(100, 40,
            cancellationToken: timeout.Token,
            configureServices: services => services.AddSingleton<NavigationManager>(navigation),
            captureTerminalOutput: true);
        await Click(terminal, "scrollable-code");
        for (var batch = 0; batch < 20; batch++)
        {
            var packet = batch % 2 == 0 ? "\u001b[<65;50;15M" : "\u001b[<64;50;15M";
            await terminal.SendTerminalInputAsync(string.Concat(Enumerable.Repeat(packet, 20)) + "\u001b[12;40R", timeout.Token);
            if (batch == 9)
            {
                terminal.Resize(80, 24);
            }
        }
        // Scroll back to the toolbar after the burst, then use mouse and keyboard.
        await terminal.SendTerminalInputAsync(string.Concat(Enumerable.Repeat("\u001b[<64;50;15M", 80)), timeout.Token);
        await terminal.WaitUntilAsync(s => s.Layouts.TryGetValue("scrollable-preview", out var box) && box.Top >= 0,
            cancellationToken: timeout.Token);
        await Click(terminal, "scrollable-preview");
        await terminal.WaitUntilAsync(s => s.ContainsText("Embedded scrollbar"), cancellationToken: timeout.Token);
        await Click(terminal, "gallery-search");
        await terminal.SendTerminalInputAsync("border", timeout.Token);
        await terminal.WaitUntilAsync(s => s.ContainsText("border") && !s.ContainsText("Small Charts"), cancellationToken: timeout.Token);
        terminal.TerminalOutput.ShouldNotBeEmpty();
        terminal.TerminalOutput.ShouldNotContain("\u001b[6n");
        terminal.TerminalOutput.ShouldNotContain("\u001b[3J");
    }
    [Theory]
    [InlineData("align", "HorizontalAlignment")]
    [InlineData("border", "BoxBorder.Rounded")]
    [InlineData("columns", "Columns")]
    [InlineData("flexbox", "FlexBox")]
    [InlineData("grid", "Grid")]
    [InlineData("padder", "Padder")]
    [InlineData("panel", "Panel")]
    [InlineData("rows", "Rows")]
    [InlineData("scrollable", "Scrollable")]
    [InlineData("textbutton", "TextButton")]
    [InlineData("textinput", "TextInput")]
    [InlineData("select", "Select")]
    [InlineData("markup", "Markup")]
    [InlineData("markdown", "Markdown")]
    [InlineData("table", "SpectreTable")]
    [InlineData("syntax", "SyntaxHighlighter")]
    [InlineData("html-inline", "strong")]
    [InlineData("html-list", "<ul>")]
    [InlineData("newline", "Newline")]
    [InlineData("figlet", "Figlet")]
    [InlineData("canvas", "SpectreCanvas")]
    [InlineData("spinner", "Spinner")]
    [InlineData("small-charts", "BarChart")]
    [InlineData("step-chart", "StepChart")]
    [InlineData("zindex", "position")]
    [InlineData("drag", "onmousedown")]
    [InlineData("cli-info", "RuntimeInformation")]
    [InlineData("issue-307-focus", "TextInput")]
    public async Task EveryRoute_PreviewFirst_CodeClick_KeyboardAndResize(string route, string code)
    {
        await using var terminal = await Start(route, 80, 24);
        terminal.Snapshot.ContainsText(" ● Preview").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.Snapshot.ContainsText("Ctrl+C quit").ShouldBeTrue();
        await Click(terminal, $"{route}-code");
        await terminal.WaitUntilAsync(s => s.ContainsText(" ● Code") && s.ContainsText(code), cancellationToken: TestContext.Current.CancellationToken);
        terminal.Resize(100, 35);
        await terminal.WaitUntilAsync(s => s.Width == 100 && s.ContainsText(code), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, $"{route}-preview");
        // Tab from Preview to Code; Enter must work without a mouse click.
        await terminal.SendTerminalInputAsync("\t\r", TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(s => s.ContainsText(" ● Code"), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("columns", "columns-expand", "false", "true")]
    [InlineData("panel", "panel-more", "32", "28")]
    [InlineData("padder", "padder-more", "new(2, 2, 2, 2)", "new(1, 1, 1, 1)")]
    [InlineData("border", "border-sample-1", "BoxBorder.Double", "BoxBorder.Rounded")]
    [InlineData("spinner", "spinner-sample-1", "BouncingBar", "Dots")]
    [InlineData("flexbox", "flexbox-sample-4", "FlexWrap.Wrap", "Home")]
    [InlineData("small-charts", "small-charts-sample-1", "BreakdownChart", "BarChart")]
    [InlineData("step-chart", "step-chart-axes", "false", "true")]
    [InlineData("zindex", "zindex-move", "left=\"3\"", "left=\"1\"")]
    public async Task PropertiesAndPresets_UpdateCode_AndReset(string route, string control, string changed, string initial)
    {
        await using var terminal = await Start(route);
        await Click(terminal, control);
        await Click(terminal, $"{route}-code");
        await terminal.WaitUntilAsync(s => s.ContainsText(changed), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, $"{route}-reset");
        await terminal.WaitUntilAsync(s => s.ContainsText(initial), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Select_ClickOption_ThenKeyboard_SelectsAndResets()
    {
        await using var terminal = await Start("select");
        await ClickText(terminal, "Panel");
        await terminal.WaitUntilAsync(s => s.ContainsText("Selected: Panel"), cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendTerminalInputAsync("\u001b[B\r", TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(s => s.ContainsText("Selected: Select"), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "select-reset");
        await ClickText(terminal, "Align");
        terminal.Snapshot.ContainsText("Selected: Align").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    [Fact]
    public async Task Scrollable_WheelIsLocal_AndResetRestoresOffset()
    {
        await using var terminal = await Start("scrollable", 100, 50);
        await ClickText(terminal, "Page: 1 / 13", wheel: true);
        await terminal.WaitUntilAsync(s => s.ContainsText("Page: 4 / 13"), cancellationToken: TestContext.Current.CancellationToken);
        terminal.GetLayout("scrollable-preview").Top!.Value.ShouldBeLessThan(10);
        await Click(terminal, "scrollable-reset");
        await terminal.WaitUntilAsync(s => s.ContainsText("Page: 1 / 13"), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ZIndex_ModalCloses_AndResetRestoresLayers()
    {
        await using var terminal = await Start("zindex");
        terminal.Snapshot.ContainsText("Layer A").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.Snapshot.ContainsText("Layer E").ShouldBeTrue(terminal.DumpDiagnostics());
        await Click(terminal, "zindex-swap");
        await terminal.WaitUntilAsync(s => s.ContainsText("Order swapped"), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "zindex-modal");
        await terminal.WaitUntilAsync(s => s.ContainsText("Close modal"), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "zindex-close");
        await terminal.WaitUntilAsync(s => !s.ContainsText("Close modal"), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "zindex-reset");
        terminal.Snapshot.ContainsText("Last action: None").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    [Fact]
    public async Task TextInput_EditAndReset_UsesRealInput()
    {
        await using var terminal = await Start("textinput");
        await ClickText(terminal, "Ada");
        foreach (var character in "Ada")
        {
            await terminal.SendTerminalInputAsync(character.ToString(), TestContext.Current.CancellationToken);
        }
        await terminal.WaitUntilAsync(s => s.ContainsText("Preview: Ada"), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "textinput-reset");
        await terminal.WaitUntilAsync(s => s.ContainsText("Preview: (none)"), cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TextButton_RealClick_AndReset()
    {
        await using var terminal = await Start("textbutton");
        await ClickText(terminal, "Toggle");
        await terminal.WaitUntilAsync(s => s.ContainsText("Button switched on."), cancellationToken: TestContext.Current.CancellationToken);
        await Click(terminal, "textbutton-reset");
        terminal.Snapshot.ContainsText("Button is off.").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    private static Task<TestTerminal> Start(string route, int width = 100, int height = 40)
        => TestTerminal.StartAsync<App>(width, height, cancellationToken: TestContext.Current.CancellationToken,
            configureServices: services =>
            {
                var navigation = new ConsoleNavigationManager();
                navigation.NavigateTo(route);
                services.AddSingleton<NavigationManager>(navigation);
            });

    private static async Task Click(TestTerminal terminal, string hook)
    {
        var box = terminal.GetLayout(hook);
        await SendClick(terminal, box.Left!.Value, box.Top!.Value);
    }

    private static async Task ClickText(TestTerminal terminal, string text, bool wheel = false)
    {
        var lines = terminal.Snapshot.ScreenText.Split('\n');
        for (var y = 0; y < lines.Length; y++)
        {
            var x = lines[y].IndexOf(text, Math.Min(27, lines[y].Length), StringComparison.Ordinal);
            if (x < 0)
            {
                continue;
            }
            if (wheel)
            {
                await terminal.SendMouseAsync(new(TerminalMouseKind.Wheel, x, y, DeltaY: 3), TestContext.Current.CancellationToken);
            }
            else
            {
                await SendClick(terminal, x, y);
            }
            return;
        }
        Assert.Fail($"Text '{text}' not found.\n{terminal.DumpDiagnostics()}");
    }

    private static async Task SendClick(TestTerminal terminal, int x, int y)
    {
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, x, y), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, x, y), TestContext.Current.CancellationToken);
    }
}
