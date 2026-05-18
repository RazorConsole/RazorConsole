// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using RazorConsole.Core.Controllers;

namespace RazorConsole.Tests.Rendering;

public sealed class ConsoleAppTests
{
    private const string RenderingPipelineEnvironmentVariableName = "RAZORCONSOLE_RENDERING_PIPELINE";

    [Fact]
    public async Task RunAsync_InvokesCustomAfterRenderCallback()
    {
        ConsoleViewResult? observed = null;
        var tcs = new TaskCompletionSource<ConsoleViewResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cts = new CancellationTokenSource();

        Func<Core.Rendering.ConsoleLiveDisplayContext, ConsoleViewResult, CancellationToken, Task>? afterRenderCallback = (context, view, _) =>
        {
            observed = view;
            tcs.TrySetResult(view);
            return Task.CompletedTask;
        };

        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.UseRazorConsole<TestComponent>();
        hostBuilder.Services.Configure<ConsoleAppOptions>(options =>
        {
            options.AfterRenderAsync = afterRenderCallback;
        });

        var host = hostBuilder.Build();

        var runTask = host.RunAsync(cts.Token);

        var result = await tcs.Task;

        cts.Cancel();
        await runTask;

        observed.ShouldNotBeNull();
        observed.ShouldBeSameAs(result);
        result.Html.ShouldContain("Callback");
    }

    [Fact]
    public async Task RunAsync_DefaultConsoleAppOptionsResolves()
    {
        using var environment = new EnvironmentVariableScope(RenderingPipelineEnvironmentVariableName, null);
        using var cts = new CancellationTokenSource();

        var hostBuilder = Host.CreateApplicationBuilder();
        hostBuilder.UseRazorConsole<TestComponent>();

        var host = hostBuilder.Build();

        var runTask = host.RunAsync(cts.Token);

        ConsoleAppOptions options = host.Services.GetRequiredService<ConsoleAppOptions>();

        cts.Cancel();
        await runTask;

        options.ShouldNotBeNull();
        options.RenderingPipeline.ShouldBe(RazorConsoleRenderingPipeline.WidgetLayout);
        options.ShouldBeEquivalentTo(new ConsoleAppOptions());
    }

    [Fact]
    public void ConsoleAppOptions_DefaultsToWidgetLayoutWhenRenderingPipelineEnvironmentVariableIsNotSet()
    {
        using var environment = new EnvironmentVariableScope(RenderingPipelineEnvironmentVariableName, null);

        var options = new ConsoleAppOptions();

        options.RenderingPipeline.ShouldBe(RazorConsoleRenderingPipeline.WidgetLayout);
    }

    [Theory]
    [InlineData("LegacySpectre")]
    [InlineData("legacy")]
    [InlineData("spectre")]
    public void ConsoleAppOptions_UsesLegacySpectreWhenRenderingPipelineEnvironmentVariableRequestsIt(string value)
    {
        using var environment = new EnvironmentVariableScope(RenderingPipelineEnvironmentVariableName, value);

        var options = new ConsoleAppOptions();

        options.RenderingPipeline.ShouldBe(RazorConsoleRenderingPipeline.LegacySpectre);
    }

    [Theory]
    [InlineData("WidgetLayout")]
    [InlineData("widget")]
    public void ConsoleAppOptions_UsesWidgetLayoutWhenRenderingPipelineEnvironmentVariableRequestsIt(string value)
    {
        using var environment = new EnvironmentVariableScope(RenderingPipelineEnvironmentVariableName, value);

        var options = new ConsoleAppOptions();

        options.RenderingPipeline.ShouldBe(RazorConsoleRenderingPipeline.WidgetLayout);
    }

    private sealed class TestComponent : ComponentBase
    {
        [Parameter]
        public string Message { get; set; } = "Callback Test";

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "span");
            builder.AddAttribute(1, "data-text", "true");
            builder.AddContent(2, Message ?? string.Empty);
            builder.CloseElement();
        }
    }

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
            => Environment.SetEnvironmentVariable(_name, _originalValue);
    }
}

