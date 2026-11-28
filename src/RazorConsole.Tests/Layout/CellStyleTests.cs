// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core.Layout;
using Spectre.Console;

namespace RazorConsole.Tests.Layout;

public sealed class CellStyleTests
{
    [Fact]
    public void SpectreColors_KeepWhetherTheyArePaletteRgbOrDefault()
    {
        SpectreCellStyle.ToCellColor(Color.Blue).ShouldBe(CellColor.FromPalette(12));
        SpectreCellStyle.ToCellColor(new Color(0, 0, 255)).ShouldBe(CellColor.FromRgb(0, 0, 255));
        SpectreCellStyle.ToCellColor(Color.Default).ShouldBe(CellColor.Default);

        // Spectre treats palette blue and #0000FF as equal; cells do not, so the terminal's theme still picks the shade.
        CellColor.FromPalette(12).ShouldNotBe(CellColor.FromRgb(0, 0, 255));
    }

    [Fact]
    public void InternalAccessors_AgreeWithTheMarkupNames_ForEveryPaletteColor()
    {
        var colors = Enumerable.Range(0, 256).Select(Color.FromInt32)
            .Append(Color.Default)
            .Append(new Color(1, 2, 3))
            .Append(Color.FromHex("#B5CEA8"));

        foreach (var color in colors)
        {
            SpectreCellStyle.ToCellColor(color).ShouldBe(SpectreCellStyle.ToCellColorByName(color), color.ToMarkup());
        }
    }

    [Fact]
    public void SpectreColors_RoundTripThroughCellColors()
    {
        foreach (var color in new[] { Color.Red, Color.Grey70, Color.Default, new Color(53, 132, 29) })
        {
            var back = SpectreCellStyle.ToSpectreColor(SpectreCellStyle.ToCellColor(color));
            back.ShouldBe(color);
            back.ToMarkup().ShouldBe(color.ToMarkup());
        }
    }

    [Fact]
    public void TerminalCell_StoresACellStyle_AndKeepsTheSpectreStyleView()
    {
        var cell = new TerminalCell("x", new Style(Color.Red, Color.Default, Decoration.Bold));

        cell.CellStyle.ShouldBe(new CellStyle(CellColor.FromPalette(9), CellColor.Default, CellDecoration.Bold));
        cell.Style.ShouldNotBeNull();
        cell.Style.Value.Foreground.ShouldBe(Color.Red);
        cell.Style.Value.Decoration.ShouldBe(Decoration.Bold);
        new TerminalCell("x", (Style?)null).Style.ShouldBeNull();
        TerminalCell.Empty.CellStyle.IsPlain.ShouldBeTrue();
    }

    [Fact]
    public void Canvas_StoresCellStyles_AndApplyBackgroundKeepsDecoration()
    {
        var canvas = new TerminalCanvas(4, 1);
        canvas.Write(0, 0, "ab", new Style(Color.Green, decoration: Decoration.Underline));

        canvas.ApplyBackground(new LayoutRect(0, 0, 1, 1), new Color(10, 20, 30));

        canvas[0, 0].CellStyle.ShouldBe(new CellStyle(CellColor.FromPalette(2), CellColor.FromRgb(10, 20, 30), CellDecoration.Underline));
        canvas[1, 0].CellStyle.ShouldBe(new CellStyle(CellColor.FromPalette(2), CellColor.Default, CellDecoration.Underline));
        canvas[2, 0].CellStyle.IsPlain.ShouldBeTrue();
    }
}
