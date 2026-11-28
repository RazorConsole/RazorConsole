// Copyright (c) RazorConsole. All rights reserved.

using System.Collections.Concurrent;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Focus;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Input;

internal sealed class TerminalKeyboardEventArgs : KeyboardEventArgs
{
    public bool Handled { get; set; }
}

internal interface IKeyboardEventDispatcher
{
    Task DispatchAsync(ulong handlerId, EventArgs eventArgs, CancellationToken cancellationToken);
}

/// <summary>
/// Sends input events to the renderer. Like Blazor's browser side, it never waits for a handler's
/// <see cref="Task"/>: the returned task completes once the handler has started (its synchronous part ran
/// on the renderer's dispatcher), so input keeps flowing while an <c>async</c> handler awaits.
/// Events still reach the dispatcher one at a time, in arrival order.
/// </summary>
internal sealed class RendererKeyboardEventDispatcher : IKeyboardEventDispatcher, IFocusEventDispatcher
{
    private readonly ConsoleRenderer _renderer;

    public RendererKeyboardEventDispatcher(ConsoleRenderer renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    public Task DispatchAsync(ulong handlerId, EventArgs eventArgs, CancellationToken cancellationToken)
    {
        if (handlerId == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(handlerId));
        }

        if (eventArgs is null)
        {
            throw new ArgumentNullException(nameof(eventArgs));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // On the dispatcher (e.g. FocusAsync from OnAfterRenderAsync) a render batch may be in progress, and Blazor would apply its diffs again in a nested batch.
        return _renderer.Dispatcher.CheckAccess()
            ? DispatchAfterCurrentWorkAsync(handlerId, eventArgs)
            : _renderer.DispatchEventAsync(handlerId, eventArgs);
    }

    private async Task DispatchAfterCurrentWorkAsync(ulong handlerId, EventArgs eventArgs)
    {
        await Task.Yield();
        await _renderer.DispatchEventAsync(handlerId, eventArgs).ConfigureAwait(false);
    }
}

internal sealed class KeyboardEventManager
{
    private readonly FocusManager _focusManager;
    // A key must not be lost: wait longer than focus events do for the render that replaced its handler.
    private static readonly TimeSpan StaleHandlerRetryTimeout = TimeSpan.FromSeconds(1);
    private readonly IKeyboardEventDispatcher _dispatcher;
    private readonly ILogger<KeyboardEventManager> _logger;
    private readonly ConcurrentDictionary<string, StringBuilder> _buffers = new(StringComparer.Ordinal);
    private volatile string? _activeFocusKey;
    private readonly ScrollInputNavigator? _scroll;

    public KeyboardEventManager(
        FocusManager focusManager,
        IKeyboardEventDispatcher dispatcher,
        ILogger<KeyboardEventManager>? logger = null,
        ScrollInputNavigator? scroll = null)
    {
        _scroll = scroll;
        _focusManager = focusManager;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<KeyboardEventManager>.Instance;

        _focusManager.FocusChanged += OnFocusChanged;
    }

    internal async Task HandleKeyAsync(ConsoleKeyInfo keyInfo, CancellationToken token)
    {
        if (!_focusManager.TryGetFocusedTarget(out var initialTarget) || initialTarget is null)
        {
            return;
        }

        // The handler may rerender this node and change its input attributes
        // (for example, Tab completion closes a menu). Capture ownership before
        // dispatch so the same key cannot also trigger default focus traversal.
        var managedKey = (keyInfo.Key != ConsoleKey.Tab || initialTarget.Attributes.GetValueOrDefault("data-manage-tab") == "true")
            && initialTarget.Attributes.TryGetValue("data-input-managed", out var managed) && managed == "true";
        var handled = await DispatchKeyboardEventAsync(initialTarget, "onkeydown", keyInfo, token).ConfigureAwait(false);

        if (managedKey || handled)
        {
            await DispatchKeyboardEventAsync(initialTarget, "onkeyup", keyInfo, token).ConfigureAwait(false);
            return;
        }

        // Keys nobody handled scroll the nearest scroll container of the focused element.
        if (_scroll?.TryScroll(initialTarget.VNodeId, keyInfo.Key) == true)
        {
            await DispatchKeyboardEventAsync(initialTarget, "onkeyup", keyInfo, token).ConfigureAwait(false);
            return;
        }

        switch (keyInfo.Key)
        {
            case ConsoleKey.Tab:
                await HandleTabAsync(keyInfo, token).ConfigureAwait(false);
                break;
            case ConsoleKey.Enter:
                await TriggerActivationAsync(initialTarget, token).ConfigureAwait(false);
                break;
            default:
                if (ShouldRaiseKeyPress(keyInfo))
                {
                    await DispatchKeyboardEventAsync(initialTarget, "onkeypress", keyInfo, token).ConfigureAwait(false);
                }

                await HandleTextInputAsync(keyInfo, token).ConfigureAwait(false);
                break;
        }

        await DispatchKeyboardEventAsync(initialTarget, "onkeyup", keyInfo, token).ConfigureAwait(false);
    }

    private async Task HandleTabAsync(ConsoleKeyInfo keyInfo, CancellationToken token)
    {
        try
        {
            if ((keyInfo.Modifiers & ConsoleModifiers.Shift) != 0)
            {
                await _focusManager.FocusPreviousAsync(token).ConfigureAwait(false);
            }
            else
            {
                await _focusManager.FocusNextAsync(token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogUnableToUpdateFocusTarget(ex);
        }
    }

    private async Task HandleTextInputAsync(ConsoleKeyInfo keyInfo, CancellationToken token)
    {
        if (!_focusManager.TryGetFocusedTarget(out var target) || target is null)
        {
            return;
        }

        if (!TryApplyKeyToBuffer(target, keyInfo, out var updatedValue))
        {
            return;
        }

        if (target.Events.TryGetEvent("oninput", out var inputEvent))
        {
            var args = new ChangeEventArgs { Value = updatedValue };
            await DispatchAsync(target, inputEvent, args, token).ConfigureAwait(false);
        }
    }

    private async Task TriggerActivationAsync(FocusManager.FocusTarget keyTarget, CancellationToken token)
    {
        // Activate only the element that received the key. When its keydown handler moved focus (for
        // example by navigating), the newly focused element must not be clicked by the same Enter.
        if (!_focusManager.TryGetFocusedTarget(out var target) || target is null || target.Key != keyTarget.Key)
        {
            return;
        }

        var value = GetCurrentValue(target);

        if (target.Events.TryGetEvent("onclick", out var clickEvent))
        {
            var clickArgs = new MouseEventArgs
            {
                Type = "click",
                Detail = 1,
                Button = 0,
            };

            await DispatchAsync(target, clickEvent, clickArgs, token).ConfigureAwait(false);
        }

        if (target.Events.TryGetEvent("onchange", out var changeEvent))
        {
            var changeArgs = new ChangeEventArgs { Value = value };
            await DispatchAsync(target, changeEvent, changeArgs, token).ConfigureAwait(false);
        }
    }

    // The focused target can be a render behind the renderer; its handler ids then belong to a replaced render.
    private async Task DispatchAsync(FocusManager.FocusTarget target, VNodeEvent @event, EventArgs args, CancellationToken token)
    {
        try
        {
            await _focusManager.DispatchToLatestRenderAsync(
                target,
                @event.Name,
                handlerId => _dispatcher.DispatchAsync(handlerId, args, token),
                StaleHandlerRetryTimeout,
                token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogFailedToDispatchHandler(ex, @event.Name);
        }
    }

    private async Task<bool> DispatchKeyboardEventAsync(FocusManager.FocusTarget target, string eventName, ConsoleKeyInfo keyInfo, CancellationToken token)
    {
        if (!target.Events.TryGetEvent(eventName, out var nodeEvent))
        {
            return false;
        }

        var args = CreateKeyboardEventArgs(keyInfo, eventName);
        await DispatchAsync(target, nodeEvent, args, token).ConfigureAwait(false);
        return args.Handled;
    }

    private bool TryApplyKeyToBuffer(FocusManager.FocusTarget target, ConsoleKeyInfo keyInfo, out string value)
    {
        var buffer = GetOrCreateBuffer(target);
        var changed = false;

        // StringBuilder is not thread-safe, but we only access it from the input thread
        // which is single-threaded in this context. However, we use ConcurrentDictionary
        // to ensure thread-safe access to the dictionary itself.
        if (keyInfo.Key == ConsoleKey.Backspace)
        {
            if (buffer.Length > 0)
            {
                buffer.Remove(buffer.Length - 1, 1);
                changed = true;
            }
        }
        else if (!char.IsControl(keyInfo.KeyChar))
        {
            buffer.Append(keyInfo.KeyChar);
            changed = true;
        }

        value = buffer.ToString();
        return changed;
    }

    private string GetCurrentValue(FocusManager.FocusTarget target)
    {
        var buffer = GetOrCreateBuffer(target);
        return buffer.ToString();
    }

    private StringBuilder GetOrCreateBuffer(FocusManager.FocusTarget target)
    {
        return _buffers.GetOrAdd(target.Key, _ => new StringBuilder(ResolveInitialValue(target)));
    }


    private static string ResolveInitialValue(FocusManager.FocusTarget target)
    {
        if (target.Attributes.TryGetValue("value", out var value) && value is not null)
        {
            return value;
        }

        return string.Empty;
    }

    private static TerminalKeyboardEventArgs CreateKeyboardEventArgs(ConsoleKeyInfo keyInfo, string eventName)
    {
        var type = eventName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
            ? eventName[2..]
            : eventName;

        type = type.ToLowerInvariant();

        return new TerminalKeyboardEventArgs
        {
            Type = type,
            Key = ResolveKeyValue(keyInfo),
            Code = ResolveKeyCode(keyInfo),
            Location = 0,
            Repeat = false,
            AltKey = (keyInfo.Modifiers & ConsoleModifiers.Alt) != 0,
            CtrlKey = (keyInfo.Modifiers & ConsoleModifiers.Control) != 0,
            MetaKey = false,
            ShiftKey = (keyInfo.Modifiers & ConsoleModifiers.Shift) != 0,
        };
    }

    private static string ResolveKeyValue(ConsoleKeyInfo keyInfo)
    {
        if (!char.IsControl(keyInfo.KeyChar) && keyInfo.KeyChar != '\0')
        {
            return keyInfo.KeyChar.ToString();
        }

        return keyInfo.Key switch
        {
            ConsoleKey.Enter => "Enter",
            ConsoleKey.Tab => "Tab",
            ConsoleKey.Backspace => "Backspace",
            ConsoleKey.Escape => "Escape",
            ConsoleKey.UpArrow => "ArrowUp",
            ConsoleKey.DownArrow => "ArrowDown",
            ConsoleKey.LeftArrow => "ArrowLeft",
            ConsoleKey.RightArrow => "ArrowRight",
            _ => keyInfo.Key.ToString(),
        };
    }

    private static string ResolveKeyCode(ConsoleKeyInfo keyInfo)
    {
        if (keyInfo.Key >= ConsoleKey.A && keyInfo.Key <= ConsoleKey.Z)
        {
            return $"Key{keyInfo.Key}";
        }

        if (keyInfo.Key >= ConsoleKey.D0 && keyInfo.Key <= ConsoleKey.D9)
        {
            var digit = (int)keyInfo.Key - (int)ConsoleKey.D0;
            return $"Digit{digit}";
        }

        return keyInfo.Key.ToString();
    }

    private static bool ShouldRaiseKeyPress(ConsoleKeyInfo keyInfo)
    {
        return !char.IsControl(keyInfo.KeyChar) && keyInfo.KeyChar != '\0';
    }

    internal static bool ShouldBatchInput(ConsoleKeyInfo keyInfo) => TerminalInputEvents.IsBatchable(keyInfo);

    internal bool IsManagedFocus()
        => _focusManager.TryGetFocusedTarget(out var target) && target is not null
            && target.Attributes.TryGetValue("data-input-managed", out var managed) && managed == "true";

    /// <summary>
    /// Handles keys that arrived together (see <see cref="KeyBatchInputEvent"/>), the way the console input loop always
    /// has: unless the focused element manages its input, a run of printable keys and Backspace is applied as one
    /// <c>oninput</c>, a key that is neither ends the run and is handled as a key press, and the keys after it are
    /// handled the same way. A run of one key is a key press.
    /// </summary>
    internal async Task HandleKeyBatchAsync(IReadOnlyList<ConsoleKeyInfo> keys, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var index = 0;
        while (index < keys.Count)
        {
            var key = keys[index++];
            if (ShouldBatchInput(key) && index < keys.Count && !IsManagedFocus())
            {
                index = await HandleTextRunAsync(key, keys, index, token).ConfigureAwait(false);
            }
            else
            {
                await HandleKeyAsync(key, token).ConfigureAwait(false);
            }
        }
    }

    // Applies firstKey and the printable keys after it as one oninput. Returns the index of the first key not handled.
    private async Task<int> HandleTextRunAsync(ConsoleKeyInfo firstKey, IReadOnlyList<ConsoleKeyInfo> keys, int index, CancellationToken token)
    {
        if (!_focusManager.TryGetFocusedTarget(out var target) || target is null)
        {
            return index;
        }

        // Apply each key to the buffer (intermediate values are discarded; the final value is dispatched).
        TryApplyKeyToBuffer(target, firstKey, out _);
        var batchCount = 1;
        while (index < keys.Count && batchCount < TerminalInputEvents.MaxBatchSize)
        {
            var nextKey = keys[index++];

            // A special key (Enter, Tab, etc.) ends the run: dispatch the text so far, then handle the key normally.
            if (!ShouldBatchInput(nextKey))
            {
                var currentValue = GetCurrentValue(target);
                if (target.Events.TryGetEvent("oninput", out var inputEvent))
                {
                    var args = new ChangeEventArgs { Value = currentValue };
                    await DispatchAsync(target, inputEvent, args, token).ConfigureAwait(false);
                }

                await HandleKeyAsync(nextKey, token).ConfigureAwait(false);
                return index;
            }

            TryApplyKeyToBuffer(target, nextKey, out _);
            batchCount++;
        }

        // Dispatch a single oninput event with the final accumulated value
        var finalValue = GetCurrentValue(target);
        if (target.Events.TryGetEvent("oninput", out var finalInputEvent))
        {
            var finalArgs = new ChangeEventArgs { Value = finalValue };
            await DispatchAsync(target, finalInputEvent, finalArgs, token).ConfigureAwait(false);
        }

        return index;
    }

    private void OnFocusChanged(object? sender, FocusChangedEventArgs e)
    {
        if (e is null)
        {
            return;
        }

        _focusManager.TryGetFocusedTarget(out var target);
        var nextKey = target?.Key;
        var previousFocusKey = Interlocked.Exchange(ref _activeFocusKey, nextKey);
        if (previousFocusKey == nextKey)
        {
            return;
        }
        // Focus notifications can arrive after input has already started. Never
        // clear a live StringBuilder or replace typed text with a stale snapshot.
        if (previousFocusKey is not null)
        {
            _buffers.TryRemove(previousFocusKey, out _);
        }

        if (target is not null)
        {
            GetOrCreateBuffer(target);
        }
    }
}
