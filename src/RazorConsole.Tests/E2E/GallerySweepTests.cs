// Copyright (c) RazorConsole. All rights reserved.

using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;

namespace RazorConsole.Tests.E2E;

/// <summary>
/// Drives the Gallery through the <c>razorconsole</c> tool in a real pseudoterminal: every page, keyboard and mouse
/// input, a resize and quitting. It checks what the terminal shows and what the app's diagnostics endpoint reports
/// (route, errors, warnings), so a change to rendering or to the output path that breaks any page fails here.
/// </summary>
/// <remarks>
/// It asserts each page's own title and description rather than whole-screen snapshots, which differ between
/// ConPTY and POSIX terminals and on pages that show the machine (CLI Info) or animate (Spinner).
/// </remarks>
[Trait("Category", "E2E")]
[Collection(GalleryWheelStressCollection.Name)]
public sealed class GallerySweepTests
{
    // Main pane's title row at 120x40: right of the 25-column sidebar and its divider.
    private const string TitleRegion = "28,2,92,1";

    private static string GalleryPath => Path.Combine(AppContext.BaseDirectory, "RazorConsole.Gallery.dll");

    [Fact]
    public async Task EveryPage_RendersItsTitle_AndHandlesKeyboardInput_WithoutErrors()
    {
        await using var cli = CliE2E.Create();
        await StartGalleryAsync(cli);

        var failures = new List<string>();
        var errorCount = 0;
        foreach (var page in GalleryPages.All)
        {
            Ok(await cli.RunAsync("--json", "route", "--go", page.Route));
            var shown = await cli.RunAsync("--json", "wait", "--text", page.Description, "--timeout", "15s");
            if (shown.ExitCode != 0)
            {
                failures.Add($"/{page.Route}: the description \"{page.Description}\" did not appear.{Environment.NewLine}{Screen(shown)}");
                continue;
            }

            var title = Ok(await cli.RunAsync("--json", "screen", "--region", TitleRegion)).GetProperty("lines")[0].GetString()!;
            if (!title.Contains(page.Title, StringComparison.Ordinal))
            {
                failures.Add($"/{page.Route}: the title row shows \"{title.TrimEnd()}\", expected \"{page.Title}\".");
            }

            // Animated pages never go quiet; the wait only paces input, so its timeout is fine.
            await SettleAsync(cli);

            // A keys command fails with process_exited (exit 6) when the input crashed the app.
            Ok(await cli.RunAsync("--json", "keys", "Tab", "Tab", "Tab", "Enter"));
            await SettleAsync(cli);

            var errors = Ok(await cli.RunAsync("--json", "errors")).GetProperty("errors");
            if (errors.GetArrayLength() > errorCount)
            {
                failures.Add($"/{page.Route}: the app reported errors:{Environment.NewLine}{Describe(errors, errorCount)}");
                errorCount = errors.GetArrayLength();
            }
        }

        var warnings = Ok(await cli.RunAsync("--json", "logs", "--level", "Warning")).GetProperty("entries");
        if (warnings.GetArrayLength() > 0)
        {
            failures.Add($"The app logged warnings or errors:{Environment.NewLine}{Describe(warnings, 0)}");
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine + Environment.NewLine, failures));
        await QuitAndCheckTerminalAsync(cli);
    }

    [Fact]
    public async Task ClickAndResize_Rerender_AndQuitting_RestoresTheTerminal()
    {
        await using var cli = CliE2E.Create();
        await StartGalleryAsync(cli);

        // A real mouse click on a property of the Align page moves the preview and its selection marker.
        Ok(await cli.RunAsync("--json", "click", "--text", "Right"));
        Ok(await cli.RunAsync("--json", "wait", "--text", "● Right", "--timeout", "10s"));

        Ok(await cli.RunAsync("--json", "resize", "80", "24"));
        Ok(await cli.RunAsync("--json", "wait", "--text", GalleryPages.Align.Description, "--timeout", "10s"));
        var small = Ok(await cli.RunAsync("--json", "screen"));
        small.GetProperty("width").GetInt32().ShouldBe(80);
        small.GetProperty("height").GetInt32().ShouldBe(24);

        Ok(await cli.RunAsync("--json", "resize", "120", "40"));
        Ok(await cli.RunAsync("--json", "wait", "--text", GalleryPages.Align.Description, "--timeout", "10s"));
        Ok(await cli.RunAsync("--json", "screen", "--region", TitleRegion)).GetProperty("lines")[0].GetString()!
            .ShouldContain(GalleryPages.Align.Title);

        Ok(await cli.RunAsync("--json", "errors")).GetProperty("errors").GetArrayLength().ShouldBe(0);
        await QuitAndCheckTerminalAsync(cli);
    }

    private static async Task StartGalleryAsync(CliE2E cli)
    {
        // Development turns the diagnostics endpoint on in the Release build CI tests.
        var start = Ok(await cli.RunAsync(
            "--json", "start", "gallery", "--dll", GalleryPath,
            "--width", "120", "--height", "40",
            "--env", "DOTNET_ENVIRONMENT=Development"));
        cli.Track(start.GetProperty("pid").GetInt32());
        Ok(await cli.RunAsync("--json", "wait", "--text", GalleryPages.Align.Description, "--timeout", "60s"));
    }

    private static async Task SettleAsync(CliE2E cli)
        => await cli.RunAsync("--json", "wait", "--idle", "300ms", "--timeout", "2s");

    // Ctrl+C ends the app; the canvas must restore the main screen and turn mouse tracking off after its last frame.
    private static async Task QuitAndCheckTerminalAsync(CliE2E cli)
    {
        Ok(await cli.RunAsync("--json", "keys", "Ctrl+C"));
        Ok(await cli.RunAsync("--json", "wait", "--exit", "--timeout", "15s")).GetProperty("exitCode").GetInt32().ShouldBe(0);

        var screen = Ok(await cli.RunAsync("--json", "screen"));
        screen.GetProperty("activeBuffer").GetString().ShouldBe("normal");
        screen.GetProperty("mouseTracking").GetArrayLength().ShouldBe(0);

        Ok(await cli.RunAsync("--json", "stop"));
        Ok(await cli.RunAsync("--json", "host", "stop"));
    }

    private static string Screen(CliRun run)
    {
        try
        {
            return run.Json.GetProperty("error").GetProperty("data").GetProperty("screen").GetString() ?? run.Describe();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return run.Describe();
        }
    }

    private static string Describe(JsonElement entries, int skip)
    {
        var text = new StringBuilder();
        foreach (var entry in entries.EnumerateArray().Skip(skip))
        {
            text.AppendLine(entry.GetRawText());
        }

        return text.ToString();
    }

    private static JsonElement Ok(CliRun run)
    {
        run.ExitCode.ShouldBe(0, run.Describe());
        var json = run.Json;
        json.GetProperty("ok").GetBoolean().ShouldBeTrue(run.Describe());
        return json;
    }
}

/// <summary>
/// Keeps <see cref="GalleryPages.All"/> in step with the Gallery, so a new page is swept too.
/// </summary>
public sealed class GalleryPagesTests
{
    [Fact]
    public void Sweep_CoversEveryGalleryRoute()
    {
        var routes = typeof(RazorConsole.Gallery.Components.App).Assembly.GetTypes()
            .SelectMany(static type => type.GetCustomAttributes<RouteAttribute>())
            .Select(static route => route.Template.Trim('/'))
            .Where(static route => route.Length > 0)
            .Order(StringComparer.Ordinal)
            .ToArray();

        GalleryPages.All.Select(static page => page.Route).Order(StringComparer.Ordinal).ToArray().ShouldBe(
            routes,
            "Add each new Gallery page to GalleryPages.All with its title and a unique piece of its description.");
    }
}

/// <summary>
/// A Gallery page: its route, the title the main pane shows, and the start of its description, which appears only
/// in the main pane (the title is also in the sidebar).
/// </summary>
internal sealed record GalleryPage(string Route, string Title, string Description);

internal static class GalleryPages
{
    public static GalleryPage Align { get; } = new("align", "Align", "Position content inside a fixed region.");

    public static IReadOnlyList<GalleryPage> All { get; } =
    [
        Align,
        new("border", "Border", "Compare border styles, color and padding."),
        new("columns", "Columns", "Columns lays out child content horizontally"),
        new("flexbox", "FlexBox", "Explore direction, spacing, alignment and wrapping."),
        new("grid", "Grid", "Grid arranges content into rows and columns"),
        new("padder", "Padder", "Padder adds outer spacing around its child content"),
        new("panel", "Panel", "Panel surfaces information inside a framed container"),
        new("rows", "Rows", "Rows stacks child content vertically"),
        new("scrollable", "Scrollable", "Wheel over each preview, or focus its scrollbar"),
        new("textbutton", "Text Button", "TextButton renders clickable text"),
        new("textinput", "Text Input", "TextInput captures user text"),
        new("select", "Select", "Select shows a focusable dropdown"),
        new("markup", "Markup", "Markup renders styled text"),
        new("markdown", "Markdown", "The Markdown component parses and renders markdown"),
        new("table", "Table", "Table renders semantic HTML table markup"),
        new("syntax", "Syntax Highlight", "The SyntaxHighlighter component renders source code"),
        new("html-inline", "HTML Inline", "HTML inline tags map to Spectre markup styles"),
        new("html-list", "HTML List", "render as styled lists with bullets or numbers"),
        new("newline", "Newline", "Newline injects a line break into the render tree"),
        new("figlet", "Figlet", "Figlet renders large ASCII art text"),
        new("canvas", "Spectre Canvas", "SpectreCanvas renders an array of pixels"),
        new("spinner", "Spinner", "Try animated spinner styles."),
        new("small-charts", "Small Charts", "Compare bar and breakdown charts with real data."),
        new("step-chart", "Step Chart", "Plot multiple series and toggle the axes."),
        new("zindex", "Z-Index", "swap their order and open a real modal"),
        new("drag", "Drag", "Drag the blue card."),
        new("issue-307-focus", "Focus diagnostic", "Check focus when inputs are inserted or removed."),
        new("cli-info", "CLI Info", "Inspect the current runtime and terminal capabilities."),
    ];
}
