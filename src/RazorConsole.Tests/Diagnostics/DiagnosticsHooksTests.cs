// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RazorConsole.Core;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Layout;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Rendering.Translation.Contexts;
using RazorConsole.Core.Vdom;
using Spectre.Console;

namespace RazorConsole.Tests.Diagnostics;

/// <summary>
/// The data hooks, enabled, and the guarantees of the disabled path (proposal §3.3).
/// </summary>
public sealed class DiagnosticsHooksTests
{
    [Fact]
    public async Task Renderer_PublishesEachFrameWithItsOwnTreeAndLayout()
    {
        var state = new DiagnosticsState();
        using var renderer = CreateRenderer(state);

        var snapshot = await renderer.MountComponentAsync<HelloComponent>(ParameterView.Empty, TestContext.Current.CancellationToken);

        var frame = state.LatestFrame.ShouldNotBeNull();
        frame.Root.ShouldBeSameAs(snapshot.Root);
        frame.Layouts.ShouldNotBeNull().ShouldContain(layout => layout.VNodeId == frame.Root!.ID);
        state.Counters.Read().Frames.ShouldBeGreaterThanOrEqualTo(1);

        var next = renderer.RefreshSnapshot();

        var nextFrame = state.LatestFrame.ShouldNotBeNull();
        nextFrame.Sequence.ShouldBe(frame.Sequence + 1);
        nextFrame.Root.ShouldBeSameAs(next.Root);
        nextFrame.Root.ShouldNotBeSameAs(frame.Root);
    }

    [Fact]
    public async Task Renderer_RecordsComponentErrors()
    {
        var state = new DiagnosticsState();
        using var renderer = CreateRenderer(state);

        await Should.ThrowAsync<InvalidOperationException>(
            () => renderer.MountComponentAsync<ThrowingComponent>(ParameterView.Empty, TestContext.Current.CancellationToken));

        var error = state.Errors.Read().ShouldHaveSingleItem().Value;
        error.Source.ShouldBe("render");
        error.Message.ShouldBe(ThrowingComponent.Message);
    }

    [Fact]
    public async Task Canvas_CountsFramesDroppedWhileTheOutputIsBusy()
    {
        var state = new DiagnosticsState();
        using var writer = new BlockingWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        using var canvas = new LiveDisplayCanvas(new ConsoleLiveDisplayOptions(), console) { Diagnostics = state };

        var busy = Task.Run(() => canvas.UpdateTarget(new Text("first")), TestContext.Current.CancellationToken);
        writer.Blocked.Wait(TestContext.Current.CancellationToken);

        canvas.UpdateTarget(new Text("second"));
        writer.Release.Set();
        await busy;

        state.Counters.Read().DroppedFrames.ShouldBe(1);
    }

    [Fact]
    public void EnabledHost_RegistersDiagnosticsAndCapturesDebugLogs()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            DisableDefaults = true,
        });
        builder.UseRazorConsole<HelloComponent>();
        using var host = builder.Build();

        var state = host.Services.GetService<DiagnosticsState>().ShouldNotBeNull();
        host.Services.GetRequiredService<ConsoleRenderer>().Diagnostics.ShouldBeSameAs(state);

        // The host's default minimum is Information; the provider keeps Debug through its own filter rule.
        host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Test").LogDebug("captured");
        state.Logs.Read().ShouldContain(entry => entry.Value.Message == "captured");
    }

    [Fact]
    public void EnabledHost_OptionsOptOutLeavesNoState()
    {
        var builder = new HostBuilder()
            .UseEnvironment(Environments.Development)
            .UseRazorConsole<HelloComponent>(b => b.ConfigureServices(services =>
                services.Configure<ConsoleAppOptions>(o => o.Diagnostics = RazorConsoleDiagnostics.Disabled)));
        using var host = builder.Build();

        host.Services.GetService<DiagnosticsState>().ShouldBeNull();
        host.Services.GetRequiredService<ConsoleRenderer>().Diagnostics.ShouldBeNull();
        host.Services.GetServices<ILoggerProvider>().OfType<DiagnosticsLoggerProvider>().ShouldHaveSingleItem()
            .CreateLogger("Test").ShouldBeSameAs(NullLogger.Instance);
    }

    [Fact]
    public void EnabledHost_DebugRuleAppliesOnlyToTheDiagnosticsProvider()
    {
        var other = new CapturingLoggerProvider();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            DisableDefaults = true,
        });
        builder.UseRazorConsole<HelloComponent>(b => b.Services.AddSingleton<ILoggerProvider>(other));
        using var host = builder.Build();

        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Test");
        logger.LogDebug("debug only");
        logger.LogInformation("information");

        // The user's provider keeps the host's minimum (Information); only the diagnostics ring sees Debug.
        other.Messages.ShouldBe(["information"]);
        host.Services.GetRequiredService<DiagnosticsState>().Logs.Read().Select(e => e.Value.Message)
            .ShouldBe(["debug only", "information"]);
    }

    [Fact]
    public void DisabledHost_RegistersNoDiagnosticsServices()
    {
        // Production and a Release entry assembly, whatever configuration the tests were built in.
        using var probe = DiagnosticsActivation.OverrideEntryAssemblyIsDebugBuild(false);

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Production,
            DisableDefaults = true,
        });
        builder.UseRazorConsole<HelloComponent>();

        builder.Services.ShouldNotContain(d =>
            d.ServiceType.Namespace == typeof(DiagnosticsState).Namespace
            || (d.ImplementationType != null && d.ImplementationType.Namespace == typeof(DiagnosticsState).Namespace));

        using var host = builder.Build();
        host.Services.GetService<DiagnosticsState>().ShouldBeNull();
        host.Services.GetRequiredService<ConsoleRenderer>().Diagnostics.ShouldBeNull();
        host.Services.GetServices<ILoggerProvider>().OfType<DiagnosticsLoggerProvider>().ShouldBeEmpty();
        host.Services.GetRequiredService<IOptions<LoggerFilterOptions>>().Value.Rules
            .ShouldNotContain(rule => rule.ProviderName == typeof(DiagnosticsLoggerProvider).FullName);
    }

    [Fact]
    public void AddRazorConsoleDiagnostics_WhenNotEnabled_RegistersNothing()
    {
        var services = new ServiceCollection();
        var before = services.Count;

        services.AddRazorConsoleDiagnostics(enabled: false);

        services.Count.ShouldBe(before);
    }

    [Fact]
    public async Task DisabledRenderer_FrameAllocationsDifferFromEnabledOnlyByTheFrameRecord()
    {
        // Every hook is a null check when disabled, so a disabled frame allocates exactly what an enabled frame
        // allocates minus the one frame record that PublishFrame creates. Anything else on the enabled path would
        // show up here as a difference, and anything diagnostics adds outside that path would be paid by both.
        using var disabled = CreateRenderer(diagnostics: null);
        var state = new DiagnosticsState();
        using var enabled = CreateRenderer(state);

        await disabled.MountComponentAsync<HelloComponent>(ParameterView.Empty, TestContext.Current.CancellationToken);
        await enabled.MountComponentAsync<HelloComponent>(ParameterView.Empty, TestContext.Current.CancellationToken);

        disabled.Diagnostics.ShouldBeNull();
        var disabledBytes = MeasureFrameAllocations(disabled);
        var enabledBytes = MeasureFrameAllocations(enabled);
        var frameRecordBytes = MeasureMinimum(() => state.PublishFrame(null, null, null));

        frameRecordBytes.ShouldBeGreaterThan(0);
        (enabledBytes - disabledBytes).ShouldBe(frameRecordBytes);
    }

    private static long MeasureFrameAllocations(ConsoleRenderer renderer) => MeasureMinimum(() => renderer.RefreshSnapshot());

    // The minimum over several warmed-up runs ignores one-off allocations such as JIT or first-use caches.
    private static long MeasureMinimum(Action action)
    {
        for (var i = 0; i < 5; i++)
        {
            action();
        }

        var minimum = long.MaxValue;
        for (var i = 0; i < 20; i++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            action();
            minimum = Math.Min(minimum, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        return minimum;
    }

    private static ConsoleRenderer CreateRenderer(DiagnosticsState? diagnostics)
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        var provider = services.BuildServiceProvider();
        return new ConsoleRenderer(
            provider,
            NullLoggerFactory.Instance,
            provider.GetRequiredService<TranslationContext>(),
            provider.GetService<ConsoleAppOptions>(),
            provider.GetService<WidgetTranslationContext>(),
            provider.GetService<LayoutEngine>(),
            provider.GetService<VNodeIdAccessor>(),
            provider.GetService<VNodeLayoutAccessor>(),
            provider.GetService<TerminalMonitor>(),
            diagnostics);
    }

    private sealed class HelloComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddContent(1, "hello");
            builder.CloseElement();
        }
    }

    private sealed class ThrowingComponent : ComponentBase
    {
        public const string Message = "render failed";

        protected override void BuildRenderTree(RenderTreeBuilder builder) => throw new InvalidOperationException(Message);
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (messages)
                {
                    messages.Add(formatter(state, exception));
                }
            }
        }
    }

    // Blocks the first write until released, so the canvas stays busy inside its output lock.
    private sealed class BlockingWriter : StringWriter
    {
        public ManualResetEventSlim Blocked { get; } = new();

        public ManualResetEventSlim Release { get; } = new();

        public override void Write(char value)
        {
            Block();
            base.Write(value);
        }

        public override void Write(string? value)
        {
            Block();
            base.Write(value);
        }

        public override void Write(char[] buffer, int index, int count)
        {
            Block();
            base.Write(buffer, index, count);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Blocked.Dispose();
                Release.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Block()
        {
            Blocked.Set();
            Release.Wait();
        }
    }
}
