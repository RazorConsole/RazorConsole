// Copyright (c) RazorConsole. All rights reserved.

using RazorConsole.Core;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Input;

namespace RazorConsole.Tests.Input;

public sealed class InputRouterTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConsoleInput_GroupsKeysThatAreAvailableTogether()
    {
        var console = new QueuedConsole("hello\tx");

        var events = await ReadAsync(new ConsoleTerminalInput(console), console, count: 2);

        // A printable first key gathers the keys after it, up to and including the first one that is not printable.
        events[0].ShouldBeOfType<KeyBatchInputEvent>().Keys.Select(k => k.KeyChar).ShouldBe("hello\t".ToCharArray());
        events[1].ShouldBeOfType<KeyInputEvent>().Key.KeyChar.ShouldBe('x');
    }

    [Fact]
    public async Task ConsoleInput_PostsAKeyThatIsNotPrintableOnItsOwn()
    {
        var console = new QueuedConsole("\tab");

        var events = await ReadAsync(new ConsoleTerminalInput(console), console, count: 2);

        events[0].ShouldBeOfType<KeyInputEvent>().Key.Key.ShouldBe(ConsoleKey.Tab);
        events[1].ShouldBeOfType<KeyBatchInputEvent>().Keys.Select(k => k.KeyChar).ShouldBe(['a', 'b']);
    }

    [Fact]
    public async Task ConsoleInput_DecodesMouseReports_WhenTheConsoleDoesNotDecodeThem()
    {
        var options = new ConsoleAppOptions();
        options.ConsoleLiveDisplayOptions.EnableMouseEvents = true;
        var console = new QueuedConsole("\u001b[<0;3;2M");

        var events = await ReadAsync(new ConsoleTerminalInput(console, options), console, count: 1);

        var mouse = events.ShouldHaveSingleItem().ShouldBeOfType<MouseInputEvent>().Mouse;
        (mouse.Kind, mouse.X, mouse.Y).ShouldBe((TerminalMouseKind.Down, 2, 1));
    }

    [Fact]
    public async Task ConsoleInput_Ends_WhenTheConsoleGoesAway()
    {
        var console = Substitute.For<IConsoleInput>();
        console.KeyAvailable.Returns(_ => throw new IOException("The console was closed."));

        await new ConsoleTerminalInput(console).RunAsync(_ => { }, Token).WaitAsync(Token);
    }

    [Fact]
    public async Task Router_Stops_WhenTheTerminalInputEnds()
    {
        var router = new InputRouter(new KeyboardEventManager(new FocusManager(), Substitute.For<IKeyboardEventDispatcher>()));
        var input = new ManualInput();
        var loop = router.RunAsync(input, Token);
        await input.Started.WaitAsync(Token);

        input.End();
        await loop.WaitAsync(Token);

        var failure = await Should.ThrowAsync<InputUnavailableException>(() => router.Submit([new KeyInputEvent(new ConsoleKeyInfo('c', ConsoleKey.C, false, false, false))]).Completion);
        failure.Reason.ShouldBe(InputUnavailableReason.Stopped);
    }

    private static async Task<List<TerminalInputEvent>> ReadAsync(ConsoleTerminalInput input, QueuedConsole console, int count)
    {
        var events = new List<TerminalInputEvent>();
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var reading = input.RunAsync(
            e =>
            {
                events.Add(e);
                if (events.Count == count)
                {
                    stop.Cancel();
                }
            },
            stop.Token);
        await reading.WaitAsync(TimeSpan.FromSeconds(10), Token);
        console.Remaining.ShouldBe(0);
        return events;
    }

    private sealed class QueuedConsole(string keys) : IConsoleInput
    {
        private readonly Queue<char> _keys = new(keys);

        public int Remaining => _keys.Count;

        public bool KeyAvailable => _keys.Count > 0;

        public ConsoleKeyInfo ReadKey(bool intercept)
        {
            var c = _keys.Dequeue();
            var key = c switch
            {
                '\t' => ConsoleKey.Tab,
                '\u001b' => ConsoleKey.Escape,
                >= 'a' and <= 'z' => ConsoleKey.A + (c - 'a'),
                _ => ConsoleKey.NoName,
            };
            return new ConsoleKeyInfo(c, key, false, false, false);
        }
    }

    private sealed class ManualInput : ITerminalInput
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Action<TerminalInputEvent>? _post;

        public Task Started => _started.Task;

        public void Post(TerminalInputEvent inputEvent) => _post!(inputEvent);

        public void End() => _ended.TrySetResult();

        public async Task RunAsync(Action<TerminalInputEvent> post, CancellationToken cancellationToken)
        {
            _post = post;
            _started.TrySetResult();
            await _ended.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
