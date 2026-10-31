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

namespace RazorConsole.Tests.Diagnostics;

/// <summary>
/// The data hooks, enabled, and the guarantees of the disabled path (proposal §3.3).
/// </summary>
public sealed class DiagnosticsHooksTests
{
    [Fact]
    public async Task Observer_PublishesEachDeliveredSnapshotWithItsOwnTreeLayoutAndSequence()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        using var renderer = CreateObservedRenderer(state);

        var snapshot = await renderer.MountComponentAsync<HelloComponent>(ParameterView.Empty, ct);
        var frame = await WaitForFrameAsync(state, snapshot.Sequence, ct);

        // The frame names the snapshot observers must have seen, which is what the focus manager waits for.
        snapshot.Sequence.ShouldBeGreaterThan(0);
        frame.RenderSequence.ShouldBe(snapshot.Sequence);
        frame.Root.ShouldBeSameAs(snapshot.Root);
        frame.Layouts.ShouldBeSameAs(snapshot.Layouts);
        frame.Layouts.ShouldNotBeNull().ShouldContain(layout => layout.VNodeId == frame.Root!.ID);
        frame.LayoutParents.ShouldBeSameAs(snapshot.LayoutParents);
        frame.Renderable.ShouldBeSameAs(snapshot.Renderable);
        state.Frames.ShouldBe(1);
    }

    [Fact]
    public async Task Observer_IgnoresASnapshotThatNoObserverIsTold()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        using var renderer = CreateObservedRenderer(state);
        var snapshot = await renderer.MountComponentAsync<HelloComponent>(ParameterView.Empty, ct);
        var frame = await WaitForFrameAsync(state, snapshot.Sequence, ct);

        // A refresh builds a snapshot for hit tests but delivers it to nobody.
        var refreshed = renderer.RefreshSnapshot();
        await Task.Delay(100, ct);

        refreshed.Sequence.ShouldBeGreaterThan(snapshot.Sequence);
        state.LatestFrame.ShouldBeSameAs(frame);
        state.Frames.ShouldBe(1);
    }

    [Fact]
    public async Task Observer_RecordsComponentErrorsFromTheRenderersOnError()
    {
        var state = new DiagnosticsState();
        using var renderer = CreateObservedRenderer(state);

        await Should.ThrowAsync<InvalidOperationException>(
            () => renderer.MountComponentAsync<ThrowingComponent>(ParameterView.Empty, TestContext.Current.CancellationToken));

        var error = state.Errors.Read().ShouldHaveSingleItem().Value;
        error.Source.ShouldBe("render");
        error.Message.ShouldBe(ThrowingComponent.Message);
    }

    [Fact]
    public async Task EnsureCurrent_PublishesAFrameForTheNewSizeOnlyAfterAResize()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        var monitor = new TerminalMonitor(80, 24);
        using var renderer = CreateObservedRenderer(state, out var observer, monitor);
        var snapshot = await renderer.MountComponentAsync<HelloComponent>(ParameterView.Empty, ct);
        var delivered = await WaitForFrameAsync(state, snapshot.Sequence, ct);
        delivered.ViewportWidth.ShouldBe(80);
        delivered.ViewportHeight.ShouldBe(24);

        // Same size: nothing to rebuild.
        observer.EnsureCurrent();
        state.LatestFrame.ShouldBeSameAs(delivered);

        monitor.Resize(100, 30);
        state.LatestFrame.ShouldBeSameAs(delivered);
        observer.EnsureCurrent();

        var resized = state.LatestFrame.ShouldNotBeNull();
        resized.ShouldNotBeSameAs(delivered);
        resized.Sequence.ShouldBe(delivered.Sequence + 1);
        resized.ViewportWidth.ShouldBe(100);
        resized.ViewportHeight.ShouldBe(30);
        resized.Root.ShouldNotBeSameAs(delivered.Root);

        // The focus manager never sees this snapshot, so the frame waits for the last one it did.
        resized.RenderSequence.ShouldBe(delivered.RenderSequence);
        state.Frames.ShouldBe(1);

        observer.EnsureCurrent();
        state.LatestFrame.ShouldBeSameAs(resized);
    }

    [Fact]
    public async Task Observer_StartedAfterARefresh_PublishesTheReplayWithNothingToWaitFor()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        using var renderer = CreateRenderer();
        var focus = new RazorConsole.Core.Focus.FocusManager();
        using var focusSubscription = renderer.Subscribe(focus);
        var mounted = await renderer.MountComponentAsync<HelloComponent>(ParameterView.Empty, ct);
        (await focus.WaitForSnapshotAsync(mounted.Sequence, TimeSpan.FromSeconds(30), ct)).ShouldBeTrue();

        // A resize or a hit test builds a snapshot that no observer is ever told about; Subscribe replays it.
        var refreshed = renderer.RefreshSnapshot();
        refreshed.Sequence.ShouldBeGreaterThan(focus.SnapshotSequence);
        using var observer = new DiagnosticsSnapshotObserver(state, renderer);
        observer.Start();

        // The focus manager can never reach the replayed sequence, so a frame carrying it would make every request
        // wait out its catch-up timeout.
        var frame = state.LatestFrame.ShouldNotBeNull();
        frame.Root.ShouldBeSameAs(refreshed.Root);
        frame.RenderSequence.ShouldBeLessThanOrEqualTo(focus.SnapshotSequence);
    }

    [Fact]
    public void Observer_NeverReplacesANewerFrameWithAnOlderSnapshot()
    {
        var state = new DiagnosticsState();
        using var renderer = CreateRenderer();
        using var observer = new DiagnosticsSnapshotObserver(state, renderer);
        var newer = new ConsoleRenderer.RenderSnapshot(VNode.CreateElement("div"), null, []) { Sequence = 5 };
        var older = new ConsoleRenderer.RenderSnapshot(VNode.CreateElement("span"), null, []) { Sequence = 3 };

        observer.OnNext(newer);
        var frame = state.LatestFrame.ShouldNotBeNull();
        observer.OnNext(older);

        state.LatestFrame.ShouldBeSameAs(frame);
        frame.RenderSequence.ShouldBe(5);
        state.Frames.ShouldBe(2);
    }

    [Fact]
    public void Observer_WithoutState_SubscribesToNothing()
    {
        using var renderer = CreateRenderer();
        var observer = new DiagnosticsSnapshotObserver(null, renderer);

        observer.Start();
        observer.EnsureCurrent();
        observer.OnError(new InvalidOperationException("ignored"));
        observer.Dispose();
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
        host.Services.GetServices<IHostedService>().OfType<DiagnosticsSnapshotObserver>().ShouldHaveSingleItem();

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
        host.Services.GetServices<IHostedService>().OfType<DiagnosticsSnapshotObserver>().ShouldBeEmpty();
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

    internal static ConsoleRenderer CreateRenderer() => CreateRenderer(out _);

    internal static ConsoleRenderer CreateRenderer(out IServiceProvider provider, TerminalMonitor? monitor = null)
    {
        var services = new ServiceCollection();
        services.AddRazorConsoleServices();
        var serviceProvider = services.BuildServiceProvider();
        provider = serviceProvider;
        return new ConsoleRenderer(
            serviceProvider,
            NullLoggerFactory.Instance,
            serviceProvider.GetRequiredService<TranslationContext>(),
            serviceProvider.GetService<ConsoleAppOptions>(),
            serviceProvider.GetService<WidgetTranslationContext>(),
            serviceProvider.GetService<LayoutEngine>(),
            serviceProvider.GetService<VNodeIdAccessor>(),
            serviceProvider.GetService<VNodeLayoutAccessor>(),
            monitor ?? serviceProvider.GetService<TerminalMonitor>());
    }

    /// <summary>A renderer whose delivered snapshots fill <paramref name="state"/>, as the hosted observer does in an app.</summary>
    internal static ConsoleRenderer CreateObservedRenderer(DiagnosticsState state)
        => CreateObservedRenderer(state, out _, out _);

    internal static ConsoleRenderer CreateObservedRenderer(DiagnosticsState state, out IServiceProvider provider)
        => CreateObservedRenderer(state, out _, out provider);

    internal static ConsoleRenderer CreateObservedRenderer(DiagnosticsState state, out DiagnosticsSnapshotObserver observer, TerminalMonitor monitor)
        => CreateObservedRenderer(state, out observer, out _, monitor);

    private static ConsoleRenderer CreateObservedRenderer(DiagnosticsState state, out DiagnosticsSnapshotObserver observer, out IServiceProvider provider, TerminalMonitor? monitor = null)
    {
        var renderer = CreateRenderer(out provider, monitor);
        observer = new DiagnosticsSnapshotObserver(state, renderer, monitor ?? provider.GetService<TerminalMonitor>());
        observer.Start();
        return renderer;
    }

    /// <summary>Renders <typeparamref name="TComponent"/> and waits until its snapshot has reached the diagnostics state.</summary>
    internal static async Task<ConsoleRenderer.RenderSnapshot> MountAsync<TComponent>(ConsoleRenderer renderer, DiagnosticsState state, CancellationToken cancellationToken)
        where TComponent : IComponent
    {
        var snapshot = await renderer.MountComponentAsync<TComponent>(ParameterView.Empty, cancellationToken);
        await WaitForFrameAsync(state, snapshot.Sequence, cancellationToken);
        return snapshot;
    }

    // Observers are told on another thread, a moment after the render returns.
    internal static async Task<DiagnosticsFrame> WaitForFrameAsync(DiagnosticsState state, long renderSequence, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (state.LatestFrame is not { } frame || frame.RenderSequence < renderSequence)
        {
            await Task.Delay(10, timeout.Token);
        }

        return state.LatestFrame;
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
}
