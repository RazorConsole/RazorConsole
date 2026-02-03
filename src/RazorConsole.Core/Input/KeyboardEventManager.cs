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

internal interface IKeyboardEventDispatcher
{
    Task DispatchAsync(ulong handlerId, EventArgs eventArgs, CancellationToken cancellationToken);
}

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
        return _renderer.DispatchEventAsync(handlerId, eventArgs);
    }
}

internal sealed class KeyboardEventManager
{
    private const int MaxPasteBatchSize = 1000;

    private readonly FocusManager _focusManager;
    private readonly IKeyboardEventDispatcher _dispatcher;
    private readonly IConsoleInput _console;
    private readonly ILogger<KeyboardEventManager> _logger;
    private readonly ConcurrentDictionary<string, StringBuilder> _buffers = new(StringComparer.Ordinal);
    private volatile string? _activeFocusKey;

    public KeyboardEventManager(
        FocusManager focusManager,
        IKeyboardEventDispatcher dispatcher,
        IConsoleInput console,
        ILogger<KeyboardEventManager>? logger = null)
    {
        _focusManager = focusManager;
        _dispatcher = dispatcher;
        _console = console;
        _logger = logger ?? NullLogger<KeyboardEventManager>.Instance;

        _focusManager.FocusChanged += OnFocusChanged;
    }

    public async Task RunAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!_console.KeyAvailable)
                {
                    await Task.Delay(50, token).ConfigureAwait(false);
                    continue;
                }

                var keyInfo = _console.ReadKey(intercept: true);

                // Check if this is a text input character and if more keys are available (paste operation)
                if (ShouldBatchInput(keyInfo) && _console.KeyAvailable)
                {
                    await HandleBatchedTextInputAsync(keyInfo, token).ConfigureAwait(false);
                }
                else
                {
                    await HandleKeyAsync(keyInfo, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (InvalidOperationException)
            {
                break;
            }
            catch (IOException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogTransientKeyboardInputFailure(ex);
                try
                {
                    await Task.Delay(200, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    internal async Task HandleKeyAsync(ConsoleKeyInfo keyInfo, CancellationToken token)
    {
        if (!_focusManager.TryGetFocusedTarget(out var initialTarget) || initialTarget is null)
        {
            return;
        }

        await DispatchKeyboardEventAsync(initialTarget, "onkeydown", keyInfo, token).ConfigureAwait(false);

        switch (keyInfo.Key)
        {
            case ConsoleKey.Tab:
                await HandleTabAsync(keyInfo, token).ConfigureAwait(false);
                break;
            case ConsoleKey.Enter:
                await TriggerActivationAsync(token).ConfigureAwait(false);
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
            await DispatchAsync(inputEvent, args, token).ConfigureAwait(false);
        }
    }

    private async Task TriggerActivationAsync(CancellationToken token)
    {
        if (!_focusManager.TryGetFocusedTarget(out var target) || target is null)
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

            await DispatchAsync(clickEvent, clickArgs, token).ConfigureAwait(false);
        }

        if (target.Events.TryGetEvent("onchange", out var changeEvent))
        {
            var changeArgs = new ChangeEventArgs { Value = value };
            await DispatchAsync(changeEvent, changeArgs, token).ConfigureAwait(false);
        }
    }

    private async Task DispatchAsync(VNodeEvent @event, EventArgs args, CancellationToken token)
    {
        try
        {
            await _dispatcher.DispatchAsync(@event.HandlerId, args, token).ConfigureAwait(false);
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
        await DispatchAsync(nodeEvent, args, token).ConfigureAwait(false);
        return true;
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
        if (target.Node.TryGetAttributeValue<string>("value", out var value) && value is not null)
        {
            return value;
        }

        return string.Empty;
    }

    private static KeyboardEventArgs CreateKeyboardEventArgs(ConsoleKeyInfo keyInfo, string eventName)
    {
        var type = eventName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
            ? eventName[2..]
            : eventName;

        type = type.ToLowerInvariant();

        return new KeyboardEventArgs
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

    private static bool ShouldBatchInput(ConsoleKeyInfo keyInfo)
    {
        // Only batch regular text input characters, not special keys
        return (!char.IsControl(keyInfo.KeyChar) && keyInfo.KeyChar != '\0') || keyInfo.Key == ConsoleKey.Backspace;
    }

    internal async Task HandleBatchedTextInputAsync(ConsoleKeyInfo firstKey, CancellationToken token)
    {
        if (!_focusManager.TryGetFocusedTarget(out var target) || target is null)
        {
            return;
        }

        // Apply the first key to the buffer (discard intermediate value, we'll get final value later)
        TryApplyKeyToBuffer(target, firstKey, out _);

        // Batch subsequent keys that are immediately available (paste operation)
        int batchCount = 1;

        while (_console.KeyAvailable && batchCount < MaxPasteBatchSize)
        {
            var nextKey = _console.ReadKey(intercept: true);

            // If we encounter a special key (Enter, Tab, etc.), stop batching and handle it normally
            if (!ShouldBatchInput(nextKey))
            {
                // Dispatch oninput with current accumulated value
                var currentValue = GetCurrentValue(target);
                if (target.Events.TryGetEvent("oninput", out var inputEvent))
                {
                    var args = new ChangeEventArgs { Value = currentValue };
                    await DispatchAsync(inputEvent, args, token).ConfigureAwait(false);
                }

                // Handle the special key normally (Enter, Tab, etc.)
                await HandleKeyAsync(nextKey, token).ConfigureAwait(false);
                return;
            }

            // Apply key to buffer (discard intermediate value, we'll get final value later)
            TryApplyKeyToBuffer(target, nextKey, out _);
            batchCount++;
        }

        // Dispatch a single oninput event with the final accumulated value
        var finalValue = GetCurrentValue(target);
        if (target.Events.TryGetEvent("oninput", out var finalInputEvent))
        {
            var finalArgs = new ChangeEventArgs { Value = finalValue };
            await DispatchAsync(finalInputEvent, finalArgs, token).ConfigureAwait(false);
        }
    }

    private void OnFocusChanged(object? sender, FocusChangedEventArgs e)
    {
        if (e is null)
        {
            return;
        }

        var previousFocusKey = _activeFocusKey;
        if (previousFocusKey is not null && _buffers.TryRemove(previousFocusKey, out var previousBuffer))
        {
            previousBuffer.Clear();
        }

        if (!_focusManager.TryGetFocusedTarget(out var target) || target is null)
        {
            _activeFocusKey = null;
            return;
        }

        _activeFocusKey = target.Key;
        var buffer = GetOrCreateBuffer(target);
        buffer.Clear();
        buffer.Append(ResolveInitialValue(target));
    }
}
