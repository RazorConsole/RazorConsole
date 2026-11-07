// Copyright (c) RazorConsole. All rights reserved.

using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RazorConsole.Components;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Vdom;
using RazorConsole.Testing;
using Spectre.Console;
using Spectre.Console.Rendering;
using BarChart = RazorConsole.Components.BarChart;
using BreakdownChart = RazorConsole.Components.BreakdownChart;

namespace RazorConsole.Tests.Components;

public sealed class ChartTests
{
    private static readonly List<StepChart.ChartSeries> Day =
    [
        new(Color.Aqua, [(0, 0), (6, 12000), (12, 3500), (18, 9000), (24, 400)]),
    ];

    [Fact]
    public async Task StepChart_WithoutWidth_FillsTheTerminal_AndFollowsItsWidth()
    {
        await using var terminal = await StartAsync<StepChart>(100, new() { [nameof(StepChart.Series)] = Day });

        XAxis(terminal.CurrentFrame).Length.ShouldBe(100, terminal.DumpDiagnostics());

        terminal.Resize(60, 24);
        await terminal.WaitUntilAsync(
            frame => frame.Width == 60 && XAxis(frame).Length == 60,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task StepChart_WithWidth_KeepsThatWidth()
    {
        await using var terminal = await StartAsync<StepChart>(100, new()
        {
            [nameof(StepChart.Width)] = 40,
            [nameof(StepChart.Series)] = Day,
        });

        // The axes take two columns beyond Width, as they always have.
        XAxis(terminal.CurrentFrame).Length.ShouldBe(42, terminal.DumpDiagnostics());
    }

    [Fact]
    public async Task StepChart_KeepsFractionalPoints_InDecimalCommaCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
        try
        {
            await using var terminal = await StartAsync<StepChart>(60, new()
            {
                [nameof(StepChart.Series)] = new List<StepChart.ChartSeries> { new(Color.Aqua, [(0, 0), (0.5, 2.5), (1, 1)]) },
            });

            terminal.CurrentFrame.ContainsText("2,5─┤").ShouldBeTrue(terminal.DumpDiagnostics());
            terminal.CurrentFrame.ContainsText("0,5").ShouldBeTrue(terminal.DumpDiagnostics());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task StepChart_FormatsAxisLabels_AndAlignsWideOnes()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            await using var terminal = await StartAsync<StepChart>(80, new()
            {
                [nameof(StepChart.XAxisFormat)] = "0'h'",
                [nameof(StepChart.YAxisFormat)] = "#,0",
                [nameof(StepChart.Series)] = Day,
            });

            var lines = terminal.CurrentFrame.ScreenText.Split('\n');
            lines.ShouldContain(line => line.StartsWith("12,000─┤", StringComparison.Ordinal), terminal.DumpDiagnostics());
            lines.ShouldContain(line => line.TrimEnd().EndsWith("24h", StringComparison.Ordinal), terminal.DumpDiagnostics());

            var axisColumns = lines
                .TakeWhile(line => !line.Contains('▶'))
                .Skip(1)
                .Select(line => line.IndexOfAny(['┤', '│']))
                .Distinct();
            axisColumns.ShouldHaveSingleItem(terminal.DumpDiagnostics()).ShouldBe(7);
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task BarChart_RendersWithoutLegacyTranslators()
    {
        await using var terminal = await StartAsync<BarChart>(40, new()
        {
            [nameof(BarChart.BarChartItems)] = new List<IBarChartItem> { new BarChartItem("Apples", 12, Color.Red) },
            [nameof(BarChart.Label)] = "Fruits",
        });

        terminal.CurrentFrame.ContainsText("Fruits").ShouldBeTrue(terminal.DumpDiagnostics());
        terminal.CurrentFrame.GetLine(1).ShouldStartWith("Apples  ███", Case.Sensitive, terminal.DumpDiagnostics());
    }

    [Fact]
    public async Task BarChart_KeepsFractionalMaxValue_InDecimalCommaCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
        try
        {
            await using var terminal = await StartAsync<BarChart>(40, new()
            {
                [nameof(BarChart.BarChartItems)] = new List<IBarChartItem> { new BarChartItem("Half", 25.25, Color.Red) },
                [nameof(BarChart.MaxValue)] = 50.5,
            });

            // 25.25 of 50.5 is half the bar, not the twentieth it is when the max value reads as 505.
            var bar = terminal.CurrentFrame.GetLine(0).Count(ch => ch == '█');
            var full = terminal.CurrentFrame.Width - "Half  ".Length;
            bar.ShouldBeInRange(full / 2 - 1, full / 2 + 1, terminal.DumpDiagnostics());
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public async Task BreakdownChart_RendersWithoutLegacyTranslators()
    {
        await using var terminal = await StartAsync<BreakdownChart>(40, new()
        {
            [nameof(BreakdownChart.BreakdownChartItems)] = new List<IBreakdownChartItem>
            {
                new BreakdownChartItem("C#", 3, Color.Purple),
                new BreakdownChartItem("Razor", 1, Color.Yellow),
            },
            [nameof(BreakdownChart.ShowTags)] = true,
        });

        terminal.CurrentFrame.GetLine(0).ShouldBe(new string('█', 40), terminal.DumpDiagnostics());
        terminal.CurrentFrame.ContainsText("Razor").ShouldBeTrue(terminal.DumpDiagnostics());
    }

    [Fact]
    public void StepChartTranslator_RendersInTheLegacyPipeline()
    {
        var chart = VNode.CreateElement("stepchart");
        chart.SetAttribute("data-width", "30");
        chart.SetAttribute("data-height", "6");
        chart.SetAttribute("data-show-axes", "true");
        var series = VNode.CreateElement("chartseries");
        series.SetAttribute("data-color", "00FFFF");
        series.SetAttribute("data-points", "0,0;1.5,2;3,1");
        chart.AddChild(series);

        var renderable = TestHelpers.CreateTestTranslationContext().Translate(chart);

        var lines = RenderLines(renderable, 80);
        lines.ShouldContain(line => line.TrimStart().StartsWith("2─┤", StringComparison.Ordinal));
        lines.Single(line => line.Contains('▶')).Length.ShouldBe(32);
    }

    private static Task<HeadlessTerminal> StartAsync<TChart>(int width, Dictionary<string, object?> parameters)
        where TChart : IComponent
        => HeadlessTerminal.StartAsync<TChart>(
            new HeadlessTerminalOptions
            {
                Width = width,
                Height = 24,
                // No legacy translators: charts must not depend on the Spectre fallback.
                ConfigureServices = services => services.Replace(ServiceDescriptor.Singleton(new WidgetTranslationContext())),
                Parameters = parameters,
            },
            TestContext.Current.CancellationToken);

    private static string XAxis(TerminalFrame frame)
        => frame.ScreenText.Split('\n').Single(line => line.Contains('▶')).TrimEnd();

    private static string[] RenderLines(IRenderable renderable, int width)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(TextWriter.Null),
        });
        var options = new RenderOptions(console.Profile.Capabilities, new Spectre.Console.Size(width, 40));
        return Segment.SplitLines(renderable.Render(options, width))
            .Select(line => string.Concat(line.Select(segment => segment.Text)).TrimEnd())
            .ToArray();
    }
}
