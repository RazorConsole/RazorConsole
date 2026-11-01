// Copyright (c) RazorConsole. All rights reserved.

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Cli.Host;
using RazorConsole.Core;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Cli;

/// <summary>
/// The app registers its endpoint before it renders anything. A tree query in that gap must wait within the timeout
/// for the first frame, not answer ok with an empty tree that reads as "the app renders nothing".
/// </summary>
public sealed class AppLayerFirstFrameTests : IAsyncDisposable
{
    private const int TestTimeoutMs = 60_000;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rc-first-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly DiagnosticsState _state = new();
    private DiagnosticsEndpointService? _service;
    private AppLayer? _layer;

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        if (_layer is not null)
        {
            await _layer.DisposeAsync();
        }

        if (_service is not null)
        {
            await _service.DisposeAsync();
        }

        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ATreeQueryBeforeTheFirstFrame_WaitsForItWithinTheTimeout()
    {
        await StartAsync();
        var query = _layer!.CallAsync(DiagnosticsAppMethods.Tree, DiagnosticsJsonContext.Default.TreeResult, TimeSpan.FromSeconds(20), Token);

        // The query is already waiting when the app renders its first frame.
        await Task.Delay(500, Token);
        query.IsCompleted.ShouldBeFalse();
        var root = VNode.CreateElement("div");
        _state.PublishFrame(root, null, null);

        var tree = await query;

        tree.Root.ShouldNotBeNull().Id.ShouldBe(root.ID);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ATreeQueryWhenNoFrameEverComes_FailsWithNotRenderedYet_NotWithAnEmptyTree()
    {
        await StartAsync();
        var watch = Stopwatch.StartNew();

        var error = await Should.ThrowAsync<DiagnosticsProtocolException>(
            () => _layer!.CallAsync(DiagnosticsAppMethods.Tree, DiagnosticsJsonContext.Default.TreeResult, TimeSpan.FromSeconds(1), Token));

        error.Code.ShouldBe(DiagnosticsErrorCodes.AppLayerUnavailable);
        error.ErrorData.ShouldNotBeNull().Deserialize(HostJsonContext.Default.LayerUnavailableData)!.Reason.ShouldBe(DiagnosticsUnavailableReasons.NotRenderedYet);
        watch.Elapsed.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(800));
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task AnAppThatRendersNothingAfterItsFirstFrame_StillAnswersWithAnEmptyTree()
    {
        await StartAsync();
        _state.PublishFrame(null, null, null);
        var watch = Stopwatch.StartNew();

        var tree = await _layer!.CallAsync(DiagnosticsAppMethods.Tree, DiagnosticsJsonContext.Default.TreeResult, TimeSpan.FromSeconds(20), Token);

        tree.Root.ShouldBeNull();
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(10));
    }

    private async Task StartAsync()
    {
        _service = new DiagnosticsEndpointService(
            _state,
            new FocusManager(),
            new ConsoleNavigationManager(),
            new ConsoleAppOptions(),
            NullLogger<DiagnosticsEndpointService>.Instance,
            new DiagnosticsEndpointSettings { StateDirectory = _directory });
        await _service.StartAsync(Token);
        var info = new DiagnosticsDiscoveryInfo(Environment.ProcessId, DateTimeOffset.UtcNow, DiagnosticsProtocol.Version, _service.Endpoint!, "TestApp", "1.0.0");
        _layer = new AppLayer(() => info, () => true, () => null);
    }
}
