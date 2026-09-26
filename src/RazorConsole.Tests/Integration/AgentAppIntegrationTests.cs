// Copyright (c) RazorConsole. All rights reserved.

using LLMAgentTUI.Components;
using LLMAgentTUI.Services;
using Microsoft.Extensions.DependencyInjection;
using RazorConsole.Core.Input;
using RazorConsole.Tests.Integration.Infrastructure;
using Spectre.Console;

namespace RazorConsole.Tests.Integration;

public sealed class AgentAppIntegrationTests
{
    [Fact]
    public async Task MarkdownTable_ReflowsOnResize_AndRetainsSelectableStyledCells()
    {
        var session = new ScriptedAgentSession([new(AgentEntryKind.Assistant,
            "| 工具 | 用途 |\n|---|---|\n| `read` | 读取工作区中的 UTF-8 文本文件，支持分页读取长文件，不执行任何 shell 命令。 |\n| `write` | Create files |")]);
        await using var terminal = await Start(session, 87, 40);
        var y = Enumerable.Range(0, 40).Single(row => terminal.Snapshot.GetLine(row).Contains("read"));
        var x = terminal.Snapshot.GetLine(y).IndexOf("read", StringComparison.Ordinal);
        terminal.Snapshot[x, y].Style!.Foreground.ShouldBe(new Color(53, 132, 29));
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, x, y), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, x + 4, y), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, x + 4, y), TestContext.Current.CancellationToken);
        _view.Display.Selection.ShouldBe("read");
        var wide = _view.Display.Text;
        terminal.Resize(40, 40);
        await terminal.WaitUntilAsync(_ => _view.Display.Text != wide, description: "table reflow at 40 columns", cancellationToken: TestContext.Current.CancellationToken);
        _view.Display.Text.ShouldNotContain("|");
        _view.Display.Text.Split('\n').ShouldAllBe(line => TextSelectionState.CellWidth(line) <= 40);
        terminal.Snapshot.ContainsText("write").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    [Fact]
    public async Task ListContinuations_AlignWithItemText_NotMessageBullet()
    {
        var session = new ScriptedAgentSession([new(AgentEntryKind.Assistant, "Intro\n\n- abcdefghijklmnopqrstuvwxyz abcdefghijklmnopqrstuvwxyz\n- second item")]);
        await using var terminal = await Start(session, 40, 30);
        var y = Enumerable.Range(0, 30).Single(row => terminal.Snapshot.GetLine(row).Contains("• abcdefghijklmnopqrstuvwxyz"));
        terminal.Snapshot.GetLine(y).ShouldStartWith("  • ");
        terminal.Snapshot.GetLine(y + 1).ShouldStartWith("    abcdefghijklmnopqrstuvwxyz");
        terminal.Snapshot.GetLine(y + 2).ShouldStartWith("  • second item");
    }

    [Fact]
    public async Task SlashMenu_HasFullRowHighlight_FiltersAndCompletesWithoutLosingFocus()
    {
        await using var terminal = await Start(new ScriptedAgentSession(), 87, 35);
        await terminal.SendKeyAsync(ConsoleKey.Oem2, '/', cancellationToken: TestContext.Current.CancellationToken);
        var menu = terminal.GetLayout("command-menu");
        menu.Height.ShouldBe(3);
        (menu.Top + menu.Height).ShouldBe(terminal.GetLayout("composer-surface").Top);
        for (var x = 0; x < 87; x++)
        {
            terminal.Snapshot[x, menu.Top!.Value].Style!.Background.ShouldBe(new Color(163, 204, 250));
        }
        terminal.Snapshot.GetLine(menu.Top!.Value).ShouldStartWith("› /permissions   choose");
        await terminal.SendKeyAsync(ConsoleKey.DownArrow, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendKeyAsync(ConsoleKey.Tab, cancellationToken: TestContext.Current.CancellationToken);
        _view.Draft.Text.ShouldBe("/usage");
        terminal.CurrentFocusKey.ShouldBe("composer");
        _view.Draft.SetText("");
        await terminal.SendKeyAsync(ConsoleKey.Oem2, '/', cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendKeyAsync(ConsoleKey.P, 'p', cancellationToken: TestContext.Current.CancellationToken);
        terminal.GetLayout("command-menu").Height.ShouldBe(1);
        await terminal.SendKeyAsync(ConsoleKey.Escape, cancellationToken: TestContext.Current.CancellationToken);
        _view.Draft.Text.ShouldBe("/p");
    }

    [Fact]
    public async Task SlashMenu_MouseSelection_ExecutesLocalCommand()
    {
        var session = new ScriptedAgentSession();
        await using var terminal = await Start(session, 87, 35);
        await terminal.SendKeyAsync(ConsoleKey.Oem2, '/', cancellationToken: TestContext.Current.CancellationToken);
        var menu = terminal.GetLayout("command-/permissions");
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, 25, menu.Top!.Value), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, 25, menu.Top!.Value), TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(_ => terminal.CurrentFocusKey == "permissions", cancellationToken: TestContext.Current.CancellationToken);
        session.Transcript.ShouldBeEmpty();
    }

    [Fact]
    public async Task Transcript_StylesAreRealSelectableCells_AndToolDisclosureWorks()
    {
        var session = new ScriptedAgentSession([
            new(AgentEntryKind.User, "hello"),
            new(AgentEntryKind.Assistant, "**Bold** and `code`"),
            new(AgentEntryKind.Tool, "Ran ls", "one\ntwo\nthree\nfour"),
            new(AgentEntryKind.Error, "request failed")]);
        await using var terminal = await Start(session, 87, 40);
        var top = terminal.GetLayout("transcript").Top!.Value;
        for (var y = top; y < top + 3; y++)
        {
            terminal.Snapshot[70, y].Style!.Background.ShouldBe(new Color(239, 239, 241));
        }
        var bold = Enumerable.Range(0, 40).Single(y => terminal.Snapshot.GetLine(y).Contains("Bold and code"));
        terminal.Snapshot[2, bold].Style!.Decoration.ShouldBe(Decoration.Bold);
        terminal.Snapshot[11, bold].Style!.Foreground.ShouldBe(new Color(53, 132, 29));
        var details = Enumerable.Range(0, 40).Single(y => terminal.Snapshot.GetLine(y).Contains("+ Show details"));
        terminal.Snapshot[2, details].Style!.Foreground.ShouldBe(new Color(17, 96, 220));
        terminal.Snapshot.ContainsText("four").ShouldBeFalse();
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, 3, details), TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("four").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.Snapshot.ContainsText("− Hide details").ShouldBeTrue();
        var error = Enumerable.Range(0, 40).Single(y => terminal.Snapshot.GetLine(y).Contains("request failed"));
        terminal.Snapshot[2, error].Style!.Foreground.ShouldBe(Color.Red);
        _view.Display.Text.ShouldNotContain("**");
    }

    [Theory]
    [InlineData(40, 24)]
    [InlineData(87, 35)]
    public async Task PermissionMenu_ResizesAndHighlightsCurrentChoice(int width, int height)
    {
        await using var terminal = await Start(new ScriptedAgentSession(), width, height);
        _view.Draft.SetText("/permission");
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(_ => terminal.CurrentFocusKey == "permissions", description: "permission menu focus", cancellationToken: TestContext.Current.CancellationToken);
        var bounds = terminal.GetLayout("permission-0");
        terminal.Snapshot[0, bounds.Top!.Value].Style!.Background.ShouldBe(new Color(163, 204, 250));
        terminal.Snapshot.ContainsText("(current)").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.Resize(width + 5, height + 2);
        terminal.Snapshot.ContainsText("Update Model Permissions").ShouldBeTrue(terminal.DumpDiagnostics());
        await terminal.SendKeyAsync(ConsoleKey.Escape, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(_ => terminal.CurrentFocusKey == "composer", description: "composer focus after closing permissions", cancellationToken: TestContext.Current.CancellationToken);
        terminal.CurrentFocusKey.ShouldBe("composer");
    }

    [Fact]
    public async Task PermissionMenu_UsesKeyboardAndMouse_RequiresFullAccessConfirmation()
    {
        var session = new ScriptedAgentSession();
        await using var terminal = await Start(session, 87, 35);
        _view.Draft.SetText("/permission");
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(_ => terminal.CurrentFocusKey == "permissions", description: "permission menu focus", cancellationToken: TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("Update Model Permissions").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.CurrentFocusKey.ShouldBe("permissions");
        session.Transcript.ShouldBeEmpty();
        var option = terminal.GetLayout("permission-2");
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, option.Left!.Value, option.Top!.Value), TestContext.Current.CancellationToken);
        session.Permission.ShouldBe(ToolPermission.AskForApproval);
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        session.Permission.ShouldBe(ToolPermission.AskForApproval);
        terminal.Snapshot.ContainsText("Enter again").ShouldBeTrue(terminal.DumpDiagnostics());
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        session.Permission.ShouldBe(ToolPermission.FullAccess);
        await terminal.WaitUntilAsync(_ => terminal.CurrentFocusKey == "composer", description: "composer focus after applying permissions", cancellationToken: TestContext.Current.CancellationToken);
        terminal.CurrentFocusKey.ShouldBe("composer");
        _view.Draft.SetText("/permission");
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.WaitUntilAsync(_ => terminal.CurrentFocusKey == "permissions", description: "reopened permission menu focus", cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendKeyAsync(ConsoleKey.UpArrow, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendKeyAsync(ConsoleKey.Escape, cancellationToken: TestContext.Current.CancellationToken);
        session.Permission.ShouldBe(ToolPermission.FullAccess);
    }

    private readonly AgentViewState _view = new() { FollowOutput = false };
    private readonly RecordingTerminalActions _actions = new();
    private Task<TestTerminal> Start(ScriptedAgentSession session, int width = 80, int height = 24)
        => TestTerminal.StartAsync<App>(width, height,
            new Dictionary<string, object?> { [nameof(App.Session)] = session, [nameof(App.ViewState)] = _view }, TestContext.Current.CancellationToken,
            services => services.AddSingleton<ITerminalActions>(_actions));

    private sealed class RecordingTerminalActions : ITerminalActions
    {
        public string? CopiedText { get; private set; }
        public string? OpenedLink { get; private set; }
        public void Copy(string text) => CopiedText = text;
        public void OpenLink(string link) => OpenedLink = link;
    }

    [Fact]
    public async Task EmptySession_MatchesReferenceChromeCellsAndStyles()
    {
        await using var terminal = await Start(new ScriptedAgentSession(), 87, 35);
        var screen = terminal.Snapshot;
        string CardRow(string text) => "│ " + text.PadRight(46) + " │";
        var expected = Enumerable.Repeat(string.Empty, 35).ToArray();
        expected[0] = "╭" + new string('─', 48) + "╮";
        expected[1] = CardRow($">_ LLMAgentTUI (v{typeof(App).Assembly.GetName().Version!.ToString(3)})");
        expected[2] = CardRow("");
        expected[3] = CardRow("model:     scripted-session   /model to change");
        expected[4] = CardRow("directory: ~/repos/RazorConsole");
        expected[5] = "╰" + new string('─', 48) + "╯";
        expected[29] = "Tip: Use /export to save your conversation as Markdown.".PadLeft(87);
        expected[31] = "› Ask LLMAgentTUI to do anything";
        expected[33] = "  scripted-session · ~/repos/RazorConsole";
        expected[34] = "  ← for agents · ? for shortcuts";
        for (var row = 0; row < screen.Height; row++)
        {
            screen.GetLine(row).TrimEnd().ShouldBe(expected[row], terminal.DumpDiagnostics());
        }
        screen[5, 1].Style!.Decoration.ShouldBe(Decoration.Bold);
        screen[2, 3].Style!.Foreground.ShouldBe(new Color(158, 158, 158));
        screen[32, 3].Style!.Foreground.ShouldBe(new Color(17, 96, 220));
        screen[2, 33].Style!.Foreground.ShouldBe(new Color(166, 105, 21));
        screen[21, 33].Style!.Foreground.ShouldBe(new Color(53, 132, 29));
        for (var row = 30; row <= 32; row++)
        {
            for (var column = 0; column < screen.Width; column++)
            {
                screen[column, row].Style!.Background.ShouldBe(new Color(239, 239, 241), $"Cell ({column},{row})");
            }
        }
        terminal.GetLayout("composer").Left.ShouldBe(2);
        terminal.GetLayout("composer").Top.ShouldBe(31);
    }

    [Theory]
    [InlineData(80, 24)]
    [InlineData(120, 40)]
    [InlineData(40, 20)]
    public async Task Layout_KeepsComposerAndFooterInsideViewport(int width, int height)
    {
        var session = new ScriptedAgentSession();
        session.SeedHistory(12);
        await using var terminal = await Start(session, width, height);
        terminal.Snapshot.ContainsText("LLMAgentTUI").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.Snapshot.ContainsText("Codex").ShouldBeFalse(terminal.DumpDiagnostics());
        var composer = terminal.GetLayout("composer");
        (composer.Top!.Value + composer.Height!.Value).ShouldBeLessThanOrEqualTo(height - 2);
        terminal.GetLayout("footer").Top.ShouldBe(height - 1, terminal.DumpDiagnostics());
        terminal.CurrentFocusKey.ShouldBe("composer");
        terminal.Resize(width - 5, height - 2);
        terminal.GetLayout("footer").Top.ShouldBe(height - 3, terminal.DumpDiagnostics());
    }

    [Fact]
    public async Task Composer_MouseSelectionThenTyping_ReplacesSelectedText()
    {
        var session = new ScriptedAgentSession();
        _view.Draft.SetText("hello world");
        await using var terminal = await Start(session);
        var bounds = terminal.GetLayout("composer");
        var x = bounds.Left!.Value;
        var y = bounds.Top!.Value;
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, x, y), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, x + 5, y), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, x + 5, y), TestContext.Current.CancellationToken);
        _view.Draft.Selection.ShouldBe("hello", terminal.DumpDiagnostics());
        await terminal.SendKeyAsync(ConsoleKey.H, 'H', cancellationToken: TestContext.Current.CancellationToken);
        _view.Draft.Text.ShouldBe("H world");
        terminal.Snapshot.ContainsText("H world").ShouldBeTrue(terminal.DumpDiagnostics());

    }

    [Fact]
    public async Task Transcript_WheelAndDragCopy_UseProductionMouseDispatch()
    {
        var session = new ScriptedAgentSession();
        session.SeedHistory(20);
        await using var terminal = await Start(session);
        var bounds = terminal.GetLayout("transcript");
        var y = bounds.Top!.Value;
        await terminal.SendMouseAsync(new(TerminalMouseKind.Wheel, 5, y, DeltaY: 3), TestContext.Current.CancellationToken);
        _view.Display.Offset.ShouldBe(3);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Wheel, 5, y, DeltaY: -3), TestContext.Current.CancellationToken);
        _view.Display.Offset.ShouldBe(0);
        y++;
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, 2, y), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, 9, y), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, 9, y), TestContext.Current.CancellationToken);
        _view.Display.Selection.ShouldBe("Inspect");
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, 9, y, Button: 2), TestContext.Current.CancellationToken);
        _actions.CopiedText.ShouldBe("Inspect");
    }

    [Fact]
    public async Task ScriptedConversation_ShowsApprovalAndToolResultWithoutNetwork()
    {
        var session = new ScriptedAgentSession();
        await using var terminal = await Start(session, 100, 40);
        _view.Draft.SetText("Fix layout");
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        session.Running.ShouldBeTrue();
        for (var i = 0; i < 4; i++)
        {
            await terminal.SendKeyAsync(ConsoleKey.F6, cancellationToken: TestContext.Current.CancellationToken);
        }

        terminal.Snapshot.ContainsText("Would you like to run").ShouldBeTrue(terminal.DumpDiagnostics());
        _view.Draft.SetText("1");
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendKeyAsync(ConsoleKey.F6, cancellationToken: TestContext.Current.CancellationToken);
        await terminal.SendKeyAsync(ConsoleKey.F6, cancellationToken: TestContext.Current.CancellationToken);
        session.Running.ShouldBeFalse();
        terminal.Snapshot.ContainsText("Passed: 12").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    [Fact]
    public async Task UsageTabsAndControls_AreClickableAndWarningsOpen()
    {
        var session = new ScriptedAgentSession();
        await using var terminal = await Start(session, 100, 40);
        _view.Draft.SetText("/usage");
        await terminal.SendKeyAsync(ConsoleKey.Enter, cancellationToken: TestContext.Current.CancellationToken);
        var usage = terminal.GetLayout("usage-Usage");
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, usage.Left!.Value, usage.Top!.Value), TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("Model 0").ShouldBeTrue(terminal.DumpDiagnostics());
        var model = terminal.GetLayout("usage-Model");
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, model.Left!.Value, model.Top!.Value), TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("Model 1").ShouldBeTrue(terminal.DumpDiagnostics());
        await terminal.SendKeyAsync(ConsoleKey.Escape, cancellationToken: TestContext.Current.CancellationToken);
        var warning = terminal.GetLayout("warning-notice");
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, warning.Left!.Value + 3, warning.Top!.Value), TestContext.Current.CancellationToken);
        terminal.Snapshot.ContainsText("Session notifications").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    [Fact]
    public async Task Transcript_LinkClickOpensButDraggingDoesNot()
    {
        var session = new ScriptedAgentSession([new(AgentEntryKind.Assistant, "See https://example.com/layout for documentation.")]);
        await using var terminal = await Start(session);
        var y = terminal.GetLayout("transcript").Top!.Value;
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, 5, y), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Move, 8, y), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, 8, y), TestContext.Current.CancellationToken);
        _actions.OpenedLink.ShouldBeNull();
        await terminal.SendMouseAsync(new(TerminalMouseKind.Down, 6, y), TestContext.Current.CancellationToken);
        await terminal.SendMouseAsync(new(TerminalMouseKind.Up, 6, y), TestContext.Current.CancellationToken);
        _actions.OpenedLink.ShouldBe("https://example.com/layout");
    }
}
