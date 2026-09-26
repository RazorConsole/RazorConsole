// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using RazorConsole.Core.Input;
using RazorConsole.Tests.Integration.Infrastructure;

namespace RazorConsole.Tests.Integration;

public sealed class RenderFragmentRegionTests
{
    [Fact]
    public async Task NestedRegions_PreserveSiblingOrderAcrossInsertionAndRemoval()
    {
        await using var terminal = await TestTerminal.StartAsync<RegionContent>(20, 4,
            cancellationToken: TestContext.Current.CancellationToken);
        terminal.Snapshot.GetLine(0).TrimEnd().ShouldBe("ABC!");
        for (var i = 0; i < 4; i++)
        {
            await terminal.SendMouseAsync(new(TerminalMouseKind.Down, 0, 1), TestContext.Current.CancellationToken);
            terminal.Snapshot.GetLine(0).TrimEnd().ShouldBe(i % 2 == 0 ? "A!" : "ABC!", terminal.DumpDiagnostics());
        }
    }

    public sealed class RegionContent : ComponentBase
    {
        private bool _expanded = true;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "data-layout", "flex");
            builder.AddAttribute(2, "data-direction", "column");
            builder.OpenElement(3, "div");
            builder.AddAttribute(4, "data-layout", "flex");
            builder.AddAttribute(5, "data-direction", "row");
            builder.AddContent(6, (RenderFragment)(region =>
            {
                foreach (var letter in _expanded ? "ABC" : "A")
                {
                    region.AddContent(0, (RenderFragment)(nested => nested.AddContent(0, letter.ToString())));
                }
            }));
            builder.AddContent(7, "!");
            builder.CloseElement();
            builder.OpenElement(8, "div");
            builder.AddAttribute(9, "onmousedown", EventCallback.Factory.Create<MouseEventArgs>(this, () => _expanded = !_expanded));
            builder.AddContent(10, "toggle");
            builder.CloseElement();
            builder.CloseElement();
        }
    }
}
