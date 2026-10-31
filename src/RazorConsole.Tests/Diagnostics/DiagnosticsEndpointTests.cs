// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RazorConsole.Core;
using RazorConsole.Core.Diagnostics;
using RazorConsole.Core.Diagnostics.Protocol;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Tests.Diagnostics;

/// <summary>
/// The endpoint over a real pipe or socket, against a real renderer.
/// </summary>
public sealed class DiagnosticsEndpointTests : IAsyncDisposable
{
    private const int TestTimeoutMs = 30_000;

    private readonly string _stateDirectory = Path.Combine(Path.GetTempPath(), "rc-diag-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly List<IAsyncDisposable> _disposables = [];
    private long _nextId;

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in Enumerable.Reverse(_disposables))
        {
            await disposable.DisposeAsync();
        }

        try
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_AnswersHelloAndTheReadOnlyMethodsForTheRenderedApp()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        var focus = new FocusManager();
        using var renderer = DiagnosticsHooksTests.CreateRenderer(state);
        using var subscription = renderer.Subscribe(focus);
        await renderer.MountComponentAsync<FormComponent>(ParameterView.Empty, ct);
        var service = await StartAsync(state, focus);

        var client = await ConnectAsync(service, ct);
        var hello = await HelloAsync(client, ct);
        hello.Protocol.ShouldBe(DiagnosticsProtocol.Version);
        hello.ProcessId.ShouldBe(Environment.ProcessId);
        hello.Server.ShouldStartWith("RazorConsole.Core/");
        hello.Features.ShouldBe(DiagnosticsAppMethods.All);

        var tree = Result(await CallAsync(client, DiagnosticsAppMethods.Tree, ct), DiagnosticsJsonContext.Default.TreeResult);
        tree.Frame.ShouldBe(state.LatestFrame!.Sequence);
        tree.Root.ShouldNotBeNull().Id.ShouldBe(state.LatestFrame.Root!.ID);

        var find = Result(
            await CallAsync(client, DiagnosticsAppMethods.Find, new FindParams("key:save"), DiagnosticsJsonContext.Default.FindParams, ct),
            DiagnosticsJsonContext.Default.FindResult);
        var save = find.Matches.ShouldHaveSingleItem();
        save.Text.ShouldBe("Save");
        save.FocusKey.ShouldBe("save");
        save.Rect.ShouldNotBeNull();

        var focusResult = Result(await CallAsync(client, DiagnosticsAppMethods.Focus, ct), DiagnosticsJsonContext.Default.FocusResult);
        focusResult.Current.ShouldBe("save");
        focusResult.Targets.Select(t => t.Key).ShouldBe(["save", "cancel"]);

        Result(await CallAsync(client, DiagnosticsAppMethods.Route, ct), DiagnosticsJsonContext.Default.RouteResult).Uri.ShouldBe("app:///");
        Result(await CallAsync(client, DiagnosticsAppMethods.Stats, ct), DiagnosticsJsonContext.Default.StatsResult).Frames.ShouldBeGreaterThanOrEqualTo(1);
        Result(await CallAsync(client, DiagnosticsAppMethods.Errors, ct), DiagnosticsJsonContext.Default.ErrorsResult).Errors.ShouldBeEmpty();
        Result(await CallAsync(client, DiagnosticsAppMethods.Logs, ct), DiagnosticsJsonContext.Default.LogsResult).Next.ShouldBe(1);

        var at = Result(
            await CallAsync(client, DiagnosticsAppMethods.InspectAt, new InspectAtParams(save.Rect.Left, save.Rect.Top), DiagnosticsJsonContext.Default.InspectAtParams, ct),
            DiagnosticsJsonContext.Default.InspectAtResult);
        at.Stack[0].Id.ShouldBe(save.Id);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_ReportsRequestErrorsAndKeepsTheConnection()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        var focus = new FocusManager();
        var root = VNode.CreateElement("div");
        root.AddChild(VNode.CreateElement("button", "a"));
        root.AddChild(VNode.CreateElement("button", "b"));
        state.PublishFrame(root, null, null);
        var service = await StartAsync(state, focus);
        var client = await ConnectAsync(service, ct);
        await HelloAsync(client, ct);

        Error(await CallAsync(client, "nope", ct)).Code.ShouldBe(DiagnosticsErrorCodes.MethodNotFound);
        Error(await CallAsync(client, DiagnosticsAppMethods.Find, ct)).Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);
        Error(await CallAsync(client, DiagnosticsAppMethods.Find, new FindParams("bogus"), DiagnosticsJsonContext.Default.FindParams, ct))
            .Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);
        Error(await CallAsync(client, DiagnosticsProtocol.HelloMethod, new HelloParams(DiagnosticsProtocol.Version, "test"), DiagnosticsJsonContext.Default.HelloParams, ct))
            .Code.ShouldBe(DiagnosticsErrorCodes.InvalidRequest);

        var ambiguous = Error(await CallAsync(client, DiagnosticsAppMethods.Inspect, new InspectParams("tag:button"), DiagnosticsJsonContext.Default.InspectParams, ct));
        ambiguous.Code.ShouldBe(DiagnosticsErrorCodes.AmbiguousSelector);
        ambiguous.Data.ShouldNotBeNull().Deserialize(DiagnosticsJsonContext.Default.AmbiguousSelectorData)!.Candidates.Count.ShouldBe(2);

        // A wrong type is invalid_params, and the connection still answers afterwards.
        var wrongType = new DiagnosticsMessage { Id = ++_nextId, Method = DiagnosticsAppMethods.InspectAt, Params = JsonDocument.Parse("""{"x":"left","y":1}""").RootElement };
        await client.WriteAsync(wrongType, ct);
        Error((await client.ReadAsync(ct))!).Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);

        Result(await CallAsync(client, DiagnosticsAppMethods.Stats, ct), DiagnosticsJsonContext.Default.StatsResult).Frame.ShouldBe(1);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_RequiresHelloFirst()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await StartAsync(new DiagnosticsState(), new FocusManager());
        var client = await ConnectAsync(service, ct);

        Error(await CallAsync(client, DiagnosticsAppMethods.Stats, ct)).Code.ShouldBe(DiagnosticsErrorCodes.InvalidRequest);

        await HelloAsync(client, ct);
        Result(await CallAsync(client, DiagnosticsAppMethods.Stats, ct), DiagnosticsJsonContext.Default.StatsResult).Frames.ShouldBe(0);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_ClosesTheConnectionOnAProtocolMismatch()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await StartAsync(new DiagnosticsState(), new FocusManager());
        var client = await ConnectAsync(service, ct);

        var response = await CallAsync(client, DiagnosticsProtocol.HelloMethod, new HelloParams(DiagnosticsProtocol.Version + 1, "future"), DiagnosticsJsonContext.Default.HelloParams, ct);

        Error(response).Code.ShouldBe(DiagnosticsErrorCodes.ProtocolMismatch);
        (await client.ReadAsync(ct)).ShouldBeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_ServesAtMostFourConnectionsAndAcceptsAgainWhenOneCloses()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await StartAsync(new DiagnosticsState(), new FocusManager());

        var clients = new List<DiagnosticsConnection>();
        for (var i = 0; i < DiagnosticsEndpointService.MaxConnections; i++)
        {
            var client = await ConnectAsync(service, ct);
            await HelloAsync(client, ct);
            clients.Add(client);
        }

        // Over the limit: told why and closed, without the endpoint reading anything from it.
        await using (var rejected = new DiagnosticsConnection(await DiagnosticsTransport.ConnectAsync(service.Endpoint!, TimeSpan.FromSeconds(5), ct)))
        {
            Error((await rejected.ReadAsync(ct))!).Code.ShouldBe(DiagnosticsErrorCodes.TooManyConnections);
            (await rejected.ReadAsync(ct)).ShouldBeNull();
        }

        // The served connections are unaffected.
        foreach (var client in clients)
        {
            Result(await CallAsync(client, DiagnosticsAppMethods.Stats, ct), DiagnosticsJsonContext.Default.StatsResult);
        }

        // Closing one frees its slot once the endpoint has released the connection.
        await clients[0].DisposeAsync();
        var replacement = await ConnectWhenAcceptedAsync(service, ct);
        Result(await CallAsync(replacement, DiagnosticsAppMethods.Stats, ct), DiagnosticsJsonContext.Default.StatsResult);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_WritesTheDiscoveryFileAndRemovesItOnStop()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await StartAsync(new DiagnosticsState(), new FocusManager(), sessionToken: "session-0123_abc");
        var client = await ConnectAsync(service, ct);
        await HelloAsync(client, ct);

        var path = DiagnosticsPaths.AppDiscoveryFile(_stateDirectory, Environment.ProcessId);
        service.DiscoveryFile.ShouldBe(path);
        var info = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(path, ct), DiagnosticsJsonContext.Default.DiagnosticsDiscoveryInfo).ShouldNotBeNull();
        info.Pid.ShouldBe(Environment.ProcessId);
        info.Protocol.ShouldBe(DiagnosticsProtocol.Version);
        info.Endpoint.ShouldBe(service.Endpoint);
        info.SessionToken.ShouldBe("session-0123_abc");
        info.CoreVersion.ShouldNotBeNullOrEmpty();
        using (var process = Process.GetCurrentProcess())
        {
            info.ProcessStartTime.ShouldBe(new DateTimeOffset(process.StartTime.ToUniversalTime()), TimeSpan.FromSeconds(1));
        }

        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(path).ShouldBe(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        await service.StopAsync(ct);

        File.Exists(path).ShouldBeFalse();
        Directory.GetFiles(DiagnosticsPaths.AppsDirectory(_stateDirectory), "*.json*").ShouldBeEmpty();
        (await client.ReadAsync(ct)).ShouldBeNull();
    }

    [Theory(Timeout = TestTimeoutMs)]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("")]
    public async Task Discovery_OmitsMalformedSessionTokens(string token)
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await StartAsync(new DiagnosticsState(), new FocusManager(), sessionToken: token);

        var info = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(service.DiscoveryFile!, ct), DiagnosticsJsonContext.Default.DiagnosticsDiscoveryInfo);

        info.ShouldNotBeNull().SessionToken.ShouldBeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OptedOut_StartsNoEndpointAndWritesNothing()
    {
        var service = await StartAsync(state: null, new FocusManager());

        service.Endpoint.ShouldBeNull();
        service.DiscoveryFile.ShouldBeNull();
        Directory.Exists(_stateDirectory).ShouldBeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task SecondEndpointOnTheSameName_IsLoggedAndTheAppRunsWithoutOne()
    {
        var logger = new RecordingLogger();
        var first = await StartAsync(new DiagnosticsState(), new FocusManager());
        var second = await StartAsync(new DiagnosticsState(), new FocusManager(), logger: logger);

        first.Endpoint.ShouldNotBeNull();
        second.Endpoint.ShouldBeNull();
        logger.ShouldHaveWarning<IOException>();
        await second.StopAsync(TestContext.Current.CancellationToken);
    }

    [Theory(Timeout = TestTimeoutMs)]
    [InlineData("under-a-file")]
    [InlineData("invalid-path")]
    public async Task UnusableStateDirectory_IsLoggedAndTheAppRunsWithoutAnEndpoint(string kind)
    {
        Directory.CreateDirectory(_stateDirectory);
        var file = Path.Combine(_stateDirectory, "file");
        await File.WriteAllTextAsync(file, "", TestContext.Current.CancellationToken);
        var stateDirectory = kind == "under-a-file" ? Path.Combine(file, "state") : Path.Combine(_stateDirectory, "bad\0path");
        var logger = new RecordingLogger();

        var service = await StartAsync(new DiagnosticsState(), new FocusManager(), logger: logger, stateDirectory: stateDirectory);

        service.Endpoint.ShouldBeNull();
        service.DiscoveryFile.ShouldBeNull();
        logger.ShouldHaveWarning<Exception>();
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task OnUnix_ATooLongSocketPath_IsLoggedAsAWarningAndTheAppRunsWithoutAnEndpoint()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Socket path limits are a Unix concept.");
        var logger = new RecordingLogger();

        var service = await StartAsync(new DiagnosticsState(), new FocusManager(), logger: logger, stateDirectory: Path.Combine(_stateDirectory, new string('a', 120)));

        service.Endpoint.ShouldBeNull();
        service.DiscoveryFile.ShouldBeNull();
        logger.ShouldHaveWarning<PathTooLongException>();
        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task DiscoveryFileWriteFailure_IsLoggedAndReleasesTheEndpoint()
    {
        // A directory where the discovery file belongs makes the final move fail.
        var blocker = DiagnosticsPaths.AppDiscoveryFile(_stateDirectory, Environment.ProcessId);
        Directory.CreateDirectory(Path.Combine(blocker, "occupied"));
        var logger = new RecordingLogger();

        var service = await StartAsync(new DiagnosticsState(), new FocusManager(), logger: logger);

        service.Endpoint.ShouldBeNull();
        service.DiscoveryFile.ShouldBeNull();
        logger.ShouldHaveWarning<Exception>();
        File.Exists(blocker + ".tmp").ShouldBeFalse();
        await using (DiagnosticsTransport.Listen(DiagnosticsPaths.AppEndpoint(_stateDirectory, Environment.ProcessId)))
        {
            // The failed start gave the endpoint name back.
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task TransportFailureWhileRunning_IsLoggedAndStopCompletes()
    {
        var ct = TestContext.Current.CancellationToken;
        var logger = new RecordingLogger();
        var listener = new FakeListener(_ => throw new InvalidOperationException("transport broke"));

        var service = await StartAsync(new DiagnosticsState(), new FocusManager(), logger: logger, listen: _ => listener);
        await logger.WaitForWarningAsync<InvalidOperationException>(ct);

        await service.StopAsync(ct);
        listener.Disposed.ShouldBeTrue();
        File.Exists(DiagnosticsPaths.AppDiscoveryFile(_stateDirectory, Environment.ProcessId)).ShouldBeFalse();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task ConnectionFailure_IsLoggedAndEndsOnlyThatConnection()
    {
        var ct = TestContext.Current.CancellationToken;
        var logger = new RecordingLogger();
        var accepted = 0;
        var acceptingAgain = new TaskCompletionSource();
        var listener = new FakeListener(async token =>
        {
            if (Interlocked.Increment(ref accepted) == 1)
            {
                return new FailingStream();
            }

            acceptingAgain.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            throw new UnreachableException();
        });

        var service = await StartAsync(new DiagnosticsState(), new FocusManager(), logger: logger, listen: _ => listener);
        await logger.WaitForWarningAsync<NotSupportedException>(ct);

        // The endpoint keeps accepting after the failed connection.
        await acceptingAgain.Task.WaitAsync(ct);
        await service.StopAsync(ct);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_InjectsKeysAndAnswersAfterTheHandlersRan()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        var focus = new FocusManager();
        using var renderer = DiagnosticsHooksTests.CreateRenderer(state);
        using var subscription = renderer.Subscribe(focus);
        await renderer.MountComponentAsync<FormComponent>(ParameterView.Empty, ct);
        var dispatcher = new RecordingDispatcher();
        var keyboard = new KeyboardEventManager(focus, dispatcher, IdleConsole());
        var service = await StartAsync(state, focus, keyboard: keyboard);
        StartInputLoop(keyboard);
        var client = await ConnectAsync(service, ct);

        (await HelloAsync(client, ct)).Features.ShouldBe(DiagnosticsAppMethods.AllWithInput);

        // Enter activates the focused Save button.
        var input = Result(await InputAsync(client, new InputParams(Data: "\r"), ct), DiagnosticsJsonContext.Default.InputResult);
        input.Frame.ShouldBe(state.LatestFrame!.Sequence);
        dispatcher.Events.ShouldContain(e => e.Args is MouseEventArgs);

        // Mouse input is refused while the app does not handle mouse events.
        var mouse = Error(await InputAsync(client, new InputParams(Mouse: new DiagnosticsMouseInput("click", 1, 1)), ct));
        mouse.Code.ShouldBe(DiagnosticsErrorCodes.InputUnavailable);
        mouse.Data.ShouldNotBeNull().Deserialize(DiagnosticsJsonContext.Default.DiagnosticsUnavailableData)!.Reason.ShouldBe(DiagnosticsUnavailableReasons.MouseDisabled);

        Error(await CallAsync(client, DiagnosticsAppMethods.Input, ct)).Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);
        foreach (var invalid in new[]
        {
            new InputParams(),
            new InputParams(Data: "x", TimeoutMs: 0),
            new InputParams(Data: new string('x', DiagnosticsEndpointService.MaxInputDataLength + 1)),
            new InputParams(Mouse: new DiagnosticsMouseInput("drag", 1, 1)),
            new InputParams(Mouse: new DiagnosticsMouseInput("click", 1, 1, Count: 4)),
            new InputParams(Mouse: new DiagnosticsMouseInput("down", -1, 1)),
            new InputParams(Mouse: new DiagnosticsMouseInput("down", 1, 1, Button: "fourth")),
        })
        {
            Error(await InputAsync(client, invalid, ct)).Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);
        }
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_InjectsMouseClicksWhenTheAppHandlesMouseEvents()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        var focus = new FocusManager();
        using var renderer = DiagnosticsHooksTests.CreateRenderer(state, out var provider);
        using var subscription = renderer.Subscribe(focus);
        await renderer.MountComponentAsync<FormComponent>(ParameterView.Empty, ct);
        var options = new ConsoleAppOptions();
        options.ConsoleLiveDisplayOptions.EnableMouseEvents = true;
        var dispatcher = new RecordingDispatcher();
        var mouse = new MouseEventManager(renderer, provider.GetRequiredService<VNodeLayoutAccessor>(), dispatcher, focus);
        var keyboard = new KeyboardEventManager(focus, dispatcher, IdleConsole(), mouse: mouse, options: options);
        var service = await StartAsync(state, focus, keyboard: keyboard, options: options);
        StartInputLoop(keyboard);
        var client = await ConnectAsync(service, ct);
        await HelloAsync(client, ct);
        var cancel = Result(
            await CallAsync(client, DiagnosticsAppMethods.Find, new FindParams("key:cancel"), DiagnosticsJsonContext.Default.FindParams, ct),
            DiagnosticsJsonContext.Default.FindResult).Matches.ShouldHaveSingleItem().Rect.ShouldNotBeNull();

        Result(await InputAsync(client, new InputParams(Mouse: new DiagnosticsMouseInput("click", cancel.Left, cancel.Top)), ct), DiagnosticsJsonContext.Default.InputResult);

        dispatcher.Events.ShouldContain(e => e.Args is MouseEventArgs && ((MouseEventArgs)e.Args).Type == "click");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_TimesOutInputAndSaysWhetherItWasDispatched()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        var focus = new FocusManager();
        using var renderer = DiagnosticsHooksTests.CreateRenderer(state);
        using var subscription = renderer.Subscribe(focus);
        await renderer.MountComponentAsync<FormComponent>(ParameterView.Empty, ct);
        var dispatcher = new RecordingDispatcher { Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) };
        var keyboard = new KeyboardEventManager(focus, dispatcher, IdleConsole());
        var service = await StartAsync(state, focus, keyboard: keyboard);
        var client = await ConnectAsync(service, ct);
        await HelloAsync(client, ct);

        // The input loop is not running yet: the input is withdrawn unsent.
        var unread = Error(await InputAsync(client, new InputParams(Data: "\r", TimeoutMs: 100), ct));
        unread.Code.ShouldBe(DiagnosticsErrorCodes.Timeout);
        unread.Data.ShouldNotBeNull().Deserialize(DiagnosticsJsonContext.Default.InputTimeoutData)!.Dispatched.ShouldBeFalse();

        // The handler does not finish in time.
        StartInputLoop(keyboard);
        var stuck = Error(await InputAsync(client, new InputParams(Data: "\r", TimeoutMs: 200), ct));
        stuck.Code.ShouldBe(DiagnosticsErrorCodes.Timeout);
        stuck.Data.ShouldNotBeNull().Deserialize(DiagnosticsJsonContext.Default.InputTimeoutData)!.Dispatched.ShouldBeTrue();

        // Only the second input reached the handlers; the withdrawn one was skipped.
        dispatcher.Gate.SetResult();
        Result(await InputAsync(client, new InputParams(Data: "\r"), ct), DiagnosticsJsonContext.Default.InputResult);
        dispatcher.Events.Count(e => e.Args is MouseEventArgs).ShouldBe(2);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_WithoutAnInputLoop_AdvertisesNoInputAndRefusesIt()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await StartAsync(new DiagnosticsState(), new FocusManager());
        var client = await ConnectAsync(service, ct);

        (await HelloAsync(client, ct)).Features.ShouldNotBeNull().ShouldNotContain(DiagnosticsAppMethods.Input);

        Error(await InputAsync(client, new InputParams(Data: "x"), ct)).Code.ShouldBe(DiagnosticsErrorCodes.InputUnavailable);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_ChangesFocusAndTheRouteInProcess()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        var focus = new FocusManager();
        using var renderer = DiagnosticsHooksTests.CreateRenderer(state);
        using var subscription = renderer.Subscribe(focus);
        await renderer.MountComponentAsync<FormComponent>(ParameterView.Empty, ct);
        var service = await StartAsync(state, focus, renderer: renderer);
        var client = await ConnectAsync(service, ct);

        (await HelloAsync(client, ct)).Features.ShouldBe(DiagnosticsAppMethods.Features(acceptsInput: false, canChangeState: true));

        var next = Result(await CallAsync(client, DiagnosticsAppMethods.FocusNext, ct), DiagnosticsJsonContext.Default.FocusChangeResult);
        next.Changed.ShouldBeTrue();
        next.Current.ShouldBe("cancel");
        next.Frame.ShouldBe(state.LatestFrame!.Sequence);

        Result(await CallAsync(client, DiagnosticsAppMethods.FocusPrevious, ct), DiagnosticsJsonContext.Default.FocusChangeResult).Current.ShouldBe("save");

        var set = Result(
            await CallAsync(client, DiagnosticsAppMethods.FocusSet, new FocusSetParams("cancel"), DiagnosticsJsonContext.Default.FocusSetParams, ct),
            DiagnosticsJsonContext.Default.FocusChangeResult);
        set.Changed.ShouldBeTrue();
        set.Current.ShouldBe("cancel");
        Result(await CallAsync(client, DiagnosticsAppMethods.Focus, ct), DiagnosticsJsonContext.Default.FocusResult).Current.ShouldBe("cancel");

        // An unknown key is an error, not a request that waits for a target to render.
        Error(await CallAsync(client, DiagnosticsAppMethods.FocusSet, new FocusSetParams("missing"), DiagnosticsJsonContext.Default.FocusSetParams, ct))
            .Code.ShouldBe(DiagnosticsErrorCodes.ElementNotFound);

        var navigate = Result(
            await CallAsync(client, DiagnosticsAppMethods.Navigate, new NavigateParams("counter"), DiagnosticsJsonContext.Default.NavigateParams, ct),
            DiagnosticsJsonContext.Default.NavigateResult);
        navigate.Uri.ShouldBe("app:///counter");
        navigate.Relative.ShouldBe("counter");
        Result(await CallAsync(client, DiagnosticsAppMethods.Route, ct), DiagnosticsJsonContext.Default.RouteResult).Relative.ShouldBe("counter");

        Error(await CallAsync(client, DiagnosticsAppMethods.Navigate, new NavigateParams(" "), DiagnosticsJsonContext.Default.NavigateParams, ct))
            .Code.ShouldBe(DiagnosticsErrorCodes.InvalidParams);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_WaitsForTheFocusManagerToSeeTheFrame_BeforeAnsweringWithFocusKeys()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        var focus = new FocusManager();
        using var renderer = DiagnosticsHooksTests.CreateRenderer(state);

        // The frame is published, the focus manager has not been told about it yet.
        await renderer.MountComponentAsync<FormComponent>(ParameterView.Empty, ct);
        state.LatestFrame!.RenderSequence.ShouldBeGreaterThan(0);
        focus.SnapshotSequence.ShouldBe(0);
        var service = await StartAsync(state, focus, renderer: renderer);
        var client = await ConnectAsync(service, ct);
        await HelloAsync(client, ct);

        var find = CallAsync(client, DiagnosticsAppMethods.Find, new FindParams("key:save"), DiagnosticsJsonContext.Default.FindParams, ct);
        await Task.Delay(300, ct);
        find.IsCompleted.ShouldBeFalse();

        using var subscription = renderer.Subscribe(focus);

        var match = Result(await find, DiagnosticsJsonContext.Default.FindResult).Matches.ShouldHaveSingleItem();
        match.FocusKey.ShouldBe("save");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_FocusNext_WaitsForTheFocusManager_SoItMovesBetweenTargetsAndNotOverNone()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        var focus = new FocusManager();
        using var renderer = DiagnosticsHooksTests.CreateRenderer(state);
        await renderer.MountComponentAsync<FormComponent>(ParameterView.Empty, ct);
        var service = await StartAsync(state, focus, renderer: renderer);
        var client = await ConnectAsync(service, ct);
        await HelloAsync(client, ct);

        var next = CallAsync(client, DiagnosticsAppMethods.FocusNext, ct);
        await Task.Delay(300, ct);
        next.IsCompleted.ShouldBeFalse();

        using var subscription = renderer.Subscribe(focus);

        var result = Result(await next, DiagnosticsJsonContext.Default.FocusChangeResult);
        result.Changed.ShouldBeTrue();
        result.Current.ShouldBe("cancel");
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_AnswersAfterTheCatchUpTimeout_WhenTheFocusManagerNeverSeesTheFrame()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        using var renderer = DiagnosticsHooksTests.CreateRenderer(state);
        await renderer.MountComponentAsync<FormComponent>(ParameterView.Empty, ct);
        var service = await StartAsync(state, new FocusManager(), renderer: renderer);
        var client = await ConnectAsync(service, ct);
        await HelloAsync(client, ct);

        var match = Result(
            await CallAsync(client, DiagnosticsAppMethods.Find, new FindParams("key:save"), DiagnosticsJsonContext.Default.FindParams, ct),
            DiagnosticsJsonContext.Default.FindResult).Matches.ShouldHaveSingleItem();

        match.FocusKey.ShouldBeNull();
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_WithoutARenderer_AdvertisesNoStateChangesAndRefusesThem()
    {
        var ct = TestContext.Current.CancellationToken;
        var service = await StartAsync(new DiagnosticsState(), new FocusManager());
        var client = await ConnectAsync(service, ct);

        (await HelloAsync(client, ct)).Features.ShouldNotBeNull().ShouldNotContain(DiagnosticsAppMethods.Navigate);
        Error(await CallAsync(client, DiagnosticsAppMethods.FocusNext, ct)).Code.ShouldBe(DiagnosticsErrorCodes.MethodNotFound);
    }

    [Fact(Timeout = TestTimeoutMs)]
    public async Task Endpoint_ServesTheRendererScreenAndThePaintedCell()
    {
        var ct = TestContext.Current.CancellationToken;
        var state = new DiagnosticsState();
        var focus = new FocusManager();
        using var renderer = DiagnosticsHooksTests.CreateRenderer(state);
        using var subscription = renderer.Subscribe(focus);
        await renderer.MountComponentAsync<FormComponent>(ParameterView.Empty, ct);
        var service = await StartAsync(state, focus);
        var client = await ConnectAsync(service, ct);
        await HelloAsync(client, ct);

        var unpainted = Error(await CallAsync(client, DiagnosticsAppMethods.Screen, ct));
        unpainted.Code.ShouldBe(DiagnosticsErrorCodes.ScreenUnavailable);

        DiagnosticsScreenTests.Paint(renderer.RefreshSnapshot().Renderable!);
        var screen = Result(
            await CallAsync(client, DiagnosticsAppMethods.Screen, new ScreenParams(IncludeCells: true), DiagnosticsJsonContext.Default.ScreenParams, ct),
            DiagnosticsJsonContext.Default.ScreenResult);
        screen.Source.ShouldBe("renderer");
        screen.Frame.ShouldBe(state.LatestScreen!.Frame.Sequence);
        screen.Cells.ShouldNotBeNull().Count.ShouldBe(screen.Height);
        var row = screen.Lines.ToList().FindIndex(line => line.Contains("Save", StringComparison.Ordinal));
        var column = screen.Lines[row].IndexOf("Save", StringComparison.Ordinal);

        var at = Result(
            await CallAsync(client, DiagnosticsAppMethods.InspectAt, new InspectAtParams(column, row), DiagnosticsJsonContext.Default.InspectAtParams, ct),
            DiagnosticsJsonContext.Default.InspectAtResult);
        at.Cell.ShouldNotBeNull().Text.ShouldBe("S");
        at.Stack[0].Text.ShouldBe("Save");
    }

    [Fact]
    public void EnabledHost_RegistersTheEndpointService()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = Environments.Development,
            DisableDefaults = true,
        });
        builder.UseRazorConsole<FormComponent>();
        using var host = builder.Build();

        host.Services.GetServices<IHostedService>().OfType<DiagnosticsEndpointService>().ShouldHaveSingleItem();
    }

    private async Task<DiagnosticsEndpointService> StartAsync(
        DiagnosticsState? state,
        FocusManager focus,
        string? sessionToken = null,
        RecordingLogger? logger = null,
        string? stateDirectory = null,
        Func<string, DiagnosticsListener>? listen = null,
        KeyboardEventManager? keyboard = null,
        ConsoleAppOptions? options = null,
        ConsoleRenderer? renderer = null)
    {
        var service = new DiagnosticsEndpointService(
            state,
            focus,
            new ConsoleNavigationManager(),
            options ?? new ConsoleAppOptions(),
            logger ?? new RecordingLogger(),
            new DiagnosticsEndpointSettings { StateDirectory = stateDirectory ?? _stateDirectory, SessionToken = sessionToken, Listen = listen },
            keyboard,
            renderer);
        _disposables.Add(service);
        await service.StartAsync(TestContext.Current.CancellationToken);
        return service;
    }

    // Runs the app's input loop until the test ends.
    private void StartInputLoop(KeyboardEventManager keyboard)
    {
        var stop = new CancellationTokenSource();
        var loop = keyboard.RunAsync(stop.Token);
        _disposables.Add(new AsyncDisposable(async () =>
        {
            await stop.CancelAsync();
            await loop;
            stop.Dispose();
        }));
    }

    private static IConsoleInput IdleConsole()
    {
        var console = Substitute.For<IConsoleInput>();
        console.KeyAvailable.Returns(false);
        return console;
    }

    private Task<DiagnosticsMessage> InputAsync(DiagnosticsConnection client, InputParams parameters, CancellationToken ct)
        => CallAsync(client, DiagnosticsAppMethods.Input, parameters, DiagnosticsJsonContext.Default.InputParams, ct);

    private async Task<DiagnosticsConnection> ConnectAsync(DiagnosticsEndpointService service, CancellationToken ct)
    {
        var connection = new DiagnosticsConnection(await DiagnosticsTransport.ConnectAsync(service.Endpoint!, TimeSpan.FromSeconds(5), ct));
        _disposables.Add(connection);
        return connection;
    }

    // The endpoint frees a slot after it notices the close, so a new client may be rejected briefly.
    private async Task<DiagnosticsConnection> ConnectWhenAcceptedAsync(DiagnosticsEndpointService service, CancellationToken ct)
    {
        while (true)
        {
            var connection = new DiagnosticsConnection(await DiagnosticsTransport.ConnectAsync(service.Endpoint!, TimeSpan.FromSeconds(5), ct));
            try
            {
                var response = await CallAsync(connection, DiagnosticsProtocol.HelloMethod, new HelloParams(DiagnosticsProtocol.Version, "test"), DiagnosticsJsonContext.Default.HelloParams, ct);
                if (response.Error?.Code != DiagnosticsErrorCodes.TooManyConnections)
                {
                    Result(response, DiagnosticsJsonContext.Default.HelloResult);
                    _disposables.Add(connection);
                    return connection;
                }
            }
            catch (IOException)
            {
                // Rejected and closed before the hello was written.
            }

            await connection.DisposeAsync();
            await Task.Delay(20, ct);
        }
    }

    private async Task<HelloResult> HelloAsync(DiagnosticsConnection client, CancellationToken ct)
        => Result(
            await CallAsync(client, DiagnosticsProtocol.HelloMethod, new HelloParams(DiagnosticsProtocol.Version, "test"), DiagnosticsJsonContext.Default.HelloParams, ct),
            DiagnosticsJsonContext.Default.HelloResult);

    private async Task<DiagnosticsMessage> CallAsync(DiagnosticsConnection client, string method, CancellationToken ct)
    {
        var id = ++_nextId;
        await client.WriteAsync(new DiagnosticsMessage { Id = id, Method = method }, ct);
        return await ReadResponseAsync(client, id, ct);
    }

    private async Task<DiagnosticsMessage> CallAsync<T>(DiagnosticsConnection client, string method, T parameters, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        var id = ++_nextId;
        await client.WriteAsync(DiagnosticsMessage.Request(id, method, parameters, typeInfo), ct);
        return await ReadResponseAsync(client, id, ct);
    }

    private static async Task<DiagnosticsMessage> ReadResponseAsync(DiagnosticsConnection client, long id, CancellationToken ct)
    {
        var response = (await client.ReadAsync(ct)).ShouldNotBeNull();

        // A rejection is not an answer to any request, so it has no id.
        if (response.Error?.Code != DiagnosticsErrorCodes.TooManyConnections)
        {
            response.Id.ShouldBe(id);
        }

        return response;
    }

    private static T Result<T>(DiagnosticsMessage response, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        response.Error.ShouldBeNull();
        return response.GetResult(typeInfo).ShouldNotBeNull();
    }

    private static DiagnosticsError Error(DiagnosticsMessage response)
        => response.Error.ShouldNotBeNull();

    private sealed class RecordingLogger : ILogger<DiagnosticsEndpointService>
    {
        public ConcurrentQueue<(LogLevel Level, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Enqueue((logLevel, exception));

        public void ShouldHaveWarning<TException>()
            where TException : Exception
            => Entries.ShouldContain(e => e.Level == LogLevel.Warning && e.Exception is TException);

        public async Task WaitForWarningAsync<TException>(CancellationToken ct)
            where TException : Exception
        {
            while (!Entries.Any(e => e.Level == LogLevel.Warning && e.Exception is TException))
            {
                await Task.Delay(10, ct);
            }
        }
    }

    private sealed class AsyncDisposable(Func<Task> dispose) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await dispose();
    }

    // Records dispatched events; with a gate, every dispatch waits for it.
    private sealed class RecordingDispatcher : IKeyboardEventDispatcher
    {
        private readonly ConcurrentQueue<(ulong HandlerId, EventArgs Args)> _events = new();

        public TaskCompletionSource? Gate { get; init; }

        public IReadOnlyCollection<(ulong HandlerId, EventArgs Args)> Events => _events;

        public async Task DispatchAsync(ulong handlerId, EventArgs eventArgs, CancellationToken cancellationToken)
        {
            _events.Enqueue((handlerId, eventArgs));
            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed class FakeListener(Func<CancellationToken, Task<Stream>> accept) : DiagnosticsListener
    {
        public bool Disposed { get; private set; }

        public override string Endpoint => "fake";

        public override Task<Stream> AcceptAsync(CancellationToken cancellationToken) => accept(cancellationToken);

        public override ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    // Fails the read with an error no transport raises.
    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("stream broke");
    }

    private sealed class FormComponent : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, IVNodeIdAccessor.HookAttributeName, "form");
            Button(builder, 2, "save", "Save");
            Button(builder, 10, "cancel", "Cancel");
            builder.CloseElement();
        }

        private void Button(RenderTreeBuilder builder, int sequence, string key, string text)
        {
            builder.OpenElement(sequence, "button");
            builder.SetKey(key);
            builder.AddAttribute(sequence + 1, "onclick", EventCallback.Factory.Create<MouseEventArgs>(this, () => { }));
            builder.AddContent(sequence + 2, text);
            builder.CloseElement();
        }
    }
}
