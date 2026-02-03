// Copyright (c) RazorConsole. All rights reserved.

using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using RazorConsole.Website;
using RazorConsole.Website.Components;
[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]


Console.WriteLine("Program.cs loaded");
[SupportedOSPlatform("browser")]
public partial class Registry
{
    private static readonly Dictionary<string, IRazorConsoleRenderer> _renderers = new();
    private static readonly HashSet<string> _subscriptions = new();

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static void RegisterComponent(string elementID, int cols, int rows)
    {
        Console.WriteLine(elementID);
        switch (elementID)
        {
            case "Align":
                _renderers[elementID] = new RazorConsoleRenderer<Align_1>(elementID, cols, rows);
                break;
            case "Border":
                _renderers[elementID] = new RazorConsoleRenderer<Border_1>(elementID, cols, rows);
                break;
            case "Scrollable":
                _renderers[elementID] = new RazorConsoleRenderer<Scrollable_1>(elementID, cols, rows);
                break;
            case "Columns":
                _renderers[elementID] = new RazorConsoleRenderer<Columns_1>(elementID, cols, rows);
                break;
            case "Rows":
                _renderers[elementID] = new RazorConsoleRenderer<Rows_1>(elementID, cols, rows);
                break;
            case "Grid":
                _renderers[elementID] = new RazorConsoleRenderer<Grid_1>(elementID, cols, rows);
                break;
            case "Padder":
                _renderers[elementID] = new RazorConsoleRenderer<Padder_1>(elementID, cols, rows);
                break;
            case "TextButton":
                _renderers[elementID] = new RazorConsoleRenderer<TextButton_1>(elementID, cols, rows);
                break;
            case "TextInput":
                _renderers[elementID] = new RazorConsoleRenderer<TextInput_1>(elementID, cols, rows);
                break;
            case "Select":
                _renderers[elementID] = new RazorConsoleRenderer<Select_1>(elementID, cols, rows);
                break;
            case "Markup":
                _renderers[elementID] = new RazorConsoleRenderer<Markup_1>(elementID, cols, rows);
                break;
            case "ModalWindow":
                _renderers[elementID] = new RazorConsoleRenderer<ModalWindow_1>(elementID, cols, rows);
                break;
            case "Markdown":
                _renderers[elementID] = new RazorConsoleRenderer<Markdown_1>(elementID, cols, rows);
                break;
            case "Panel":
                _renderers[elementID] = new RazorConsoleRenderer<Panel_1>(elementID, cols, rows);
                break;
            case "Figlet":
                _renderers[elementID] = new RazorConsoleRenderer<Figlet_1>(elementID, cols, rows);
                break;
            case "SyntaxHighlighter":
                _renderers[elementID] = new RazorConsoleRenderer<SyntaxHighlighter_1>(elementID, cols, rows);
                break;
            case "Table":
                _renderers[elementID] = new RazorConsoleRenderer<Table_1>(elementID, cols, rows);
                break;
            case "Spinner":
                _renderers[elementID] = new RazorConsoleRenderer<Spinner_1>(elementID, cols, rows);
                break;
            case "Newline":
                _renderers[elementID] = new RazorConsoleRenderer<Newline_1>(elementID, cols, rows);
                break;
            case "SpectreCanvas":
                _renderers[elementID] = new RazorConsoleRenderer<SpectreCanvas_1>(elementID, cols, rows);
                break;
            case "BarChart":
                _renderers[elementID] = new RazorConsoleRenderer<BarChart_1>(elementID, cols, rows);
                break;
            case "BreakdownChart":
                _renderers[elementID] = new RazorConsoleRenderer<BreakdownChart_1>(elementID, cols, rows);
                break;
            case "StepChart":
                _renderers[elementID] = new RazorConsoleRenderer<StepChart_1>(elementID, cols, rows);
                break;
        }
    }

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static async Task HandleKeyboardEvent(string elementID, string xtermKey, string domKey, bool ctrlKey, bool altKey, bool shiftKey)
    {
        if (!_renderers.TryGetValue(elementID, out var renderer))
        {
            return;
        }
        await renderer.HandleKeyboardEventAsync(xtermKey, domKey, ctrlKey, altKey, shiftKey)
            .ConfigureAwait(false);
    }

    [JSExport]
    [SupportedOSPlatform("browser")]
    public static void HandleResize(string elementID, int cols, int rows)
    {
        if (!_renderers.TryGetValue(elementID, out var renderer))
        {
            return;
        }
        renderer.HandleResize(cols, rows);
    }
}

[SupportedOSPlatform("browser")]
public partial class XTermInterop
{
    [JSImport("writeToTerminal", "main.js")]
    public static partial void WriteToTerminal(string componentName, string data);
}
