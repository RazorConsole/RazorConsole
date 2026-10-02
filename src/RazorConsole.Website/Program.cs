// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using RazorConsole.Website;
using RazorConsole.Website.Components;
using Tutorial.Components.Chapters;
[assembly: System.Runtime.Versioning.SupportedOSPlatform("browser")]


Console.WriteLine("Program.cs loaded");
[SupportedOSPlatform("browser")]
public partial class Registry
{
    private static readonly Dictionary<string, IRazorConsoleRenderer> _renderers = new();
    private static readonly HashSet<string> _subscriptions = new();

    [JSExport]
    [SupportedOSPlatform("browser")]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RoutingHome))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RoutingSettings))]
    public static async Task RegisterComponent(string instanceID, string componentID, int cols, int rows)
    {
        await UnregisterComponent(instanceID).ConfigureAwait(false);
        Console.WriteLine($"Registering {componentID} as {instanceID}");
        switch (componentID)
        {
            case "Align":
                _renderers[instanceID] = new RazorConsoleRenderer<Align_1>(instanceID, cols, rows);
                break;
            case "Border":
                _renderers[instanceID] = new RazorConsoleRenderer<Border_1>(instanceID, cols, rows);
                break;
            case "Scrollable":
                _renderers[instanceID] = new RazorConsoleRenderer<Scrollable_1>(instanceID, cols, rows);
                break;
            case "ViewHeightScrollable":
                _renderers[instanceID] = new RazorConsoleRenderer<ViewHeightScrollable_1>(instanceID, cols, rows);
                break;
            case "Columns":
                _renderers[instanceID] = new RazorConsoleRenderer<Columns_1>(instanceID, cols, rows);
                break;
            case "Rows":
                _renderers[instanceID] = new RazorConsoleRenderer<Rows_1>(instanceID, cols, rows);
                break;
            case "Grid":
                _renderers[instanceID] = new RazorConsoleRenderer<Grid_1>(instanceID, cols, rows);
                break;
            case "Padder":
                _renderers[instanceID] = new RazorConsoleRenderer<Padder_1>(instanceID, cols, rows);
                break;
            case "TextButton":
                _renderers[instanceID] = new RazorConsoleRenderer<TextButton_1>(instanceID, cols, rows);
                break;
            case "TextInput":
                _renderers[instanceID] = new RazorConsoleRenderer<TextInput_1>(instanceID, cols, rows);
                break;
            case "Select":
                _renderers[instanceID] = new RazorConsoleRenderer<Select_1>(instanceID, cols, rows);
                break;
            case "Markup":
                _renderers[instanceID] = new RazorConsoleRenderer<Markup_1>(instanceID, cols, rows);
                break;
            case "ModalWindow":
                _renderers[instanceID] = new RazorConsoleRenderer<ModalWindow_1>(instanceID, cols, rows);
                break;
            case "Markdown":
                _renderers[instanceID] = new RazorConsoleRenderer<Markdown_1>(instanceID, cols, rows);
                break;
            case "Panel":
                _renderers[instanceID] = new RazorConsoleRenderer<Panel_1>(instanceID, cols, rows);
                break;
            case "Figlet":
                _renderers[instanceID] = new RazorConsoleRenderer<Figlet_1>(instanceID, cols, rows);
                break;
            case "SyntaxHighlighter":
                _renderers[instanceID] = new RazorConsoleRenderer<SyntaxHighlighter_1>(instanceID, cols, rows);
                break;
            case "Table":
                _renderers[instanceID] = new RazorConsoleRenderer<Table_1>(instanceID, cols, rows);
                break;
            case "Spinner":
                _renderers[instanceID] = new RazorConsoleRenderer<Spinner_1>(instanceID, cols, rows);
                break;
            case "Newline":
                _renderers[instanceID] = new RazorConsoleRenderer<Newline_1>(instanceID, cols, rows);
                break;
            case "SpectreCanvas":
                _renderers[instanceID] = new RazorConsoleRenderer<SpectreCanvas_1>(instanceID, cols, rows);
                break;
            case "BarChart":
                _renderers[instanceID] = new RazorConsoleRenderer<BarChart_1>(instanceID, cols, rows);
                break;
            case "BreakdownChart":
                _renderers[instanceID] = new RazorConsoleRenderer<BreakdownChart_1>(instanceID, cols, rows);
                break;
            case "StepChart":
                _renderers[instanceID] = new RazorConsoleRenderer<StepChart_1>(instanceID, cols, rows);
                break;
            case "FlexBox":
                _renderers[instanceID] = new RazorConsoleRenderer<FlexBox_1>(instanceID, cols, rows);
                break;
            case "HomeDemo":
                _renderers[instanceID] = new RazorConsoleRenderer<HomeDemo>(instanceID, cols, rows);
                break;
            case "TutorialHelloWorld":
                _renderers[instanceID] = new RazorConsoleRenderer<HelloWorld>(instanceID, cols, rows);
                break;
            case "TutorialStateAndEvents":
                _renderers[instanceID] = new RazorConsoleRenderer<StateAndEvents>(instanceID, cols, rows);
                break;
            case "TutorialTextInputAndFocus":
                _renderers[instanceID] = new RazorConsoleRenderer<TextInputAndFocus>(instanceID, cols, rows);
                break;
            case "TutorialMouseEvents":
                _renderers[instanceID] = new RazorConsoleRenderer<MouseEvents>(instanceID, cols, rows);
                break;
            case "TutorialWidgetLayoutAndResize":
                _renderers[instanceID] = new RazorConsoleRenderer<WidgetLayoutAndResize>(instanceID, cols, rows);
                break;
            case "TutorialRouting":
                _renderers[instanceID] = new RazorConsoleRenderer<RoutingDemo>(instanceID, cols, rows);
                break;
            case "TutorialAsyncWork":
                _renderers[instanceID] = new RazorConsoleRenderer<AsyncWork>(instanceID, cols, rows);
                break;
            case "TutorialCompleteApp":
                _renderers[instanceID] = new RazorConsoleRenderer<CompleteApp>(instanceID, cols, rows);
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
    public static async Task HandleTerminalInput(string elementID, string data)
    {
        if (_renderers.TryGetValue(elementID, out var renderer))
        {
            await renderer.HandleTerminalInputAsync(data).ConfigureAwait(false);
        }
    }

    [JSExport]
    public static async Task UnregisterComponent(string elementID)
    {
        if (_renderers.Remove(elementID, out var renderer))
        {
            await renderer.DisposeAsync().ConfigureAwait(false);
        }
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
