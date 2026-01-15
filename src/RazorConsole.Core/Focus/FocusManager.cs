// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components.Web;

using RazorConsole.Core.Controllers;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Focus;

/// <summary>
/// Tracks focusable elements within the current virtual DOM and coordinates focus changes.
/// </summary>
public sealed class FocusManager : IObserver<ConsoleRenderer.RenderSnapshot>
{
    private readonly IFocusEventDispatcher? _eventDispatcher;
#if NET9_0_OR_GREATER
    private readonly Lock _sync = new();
#else
    private readonly object _sync = new();
#endif
    private List<FocusTarget> _focuseTargets = new();
    private int _currentIndex = -1;
    private ConsoleLiveDisplayContext? _context;
    private CancellationTokenSource? _sessionCts;

    /// <summary>
    /// Raised when the focused element changes.
    /// </summary>
    public event EventHandler<FocusChangedEventArgs>? FocusChanged;

    public FocusManager()
        : this(null)
    {
    }

    internal FocusManager(IFocusEventDispatcher? eventDispatcher)
    {
        _eventDispatcher = eventDispatcher;
    }

    /// <summary>
    /// Gets the key of the currently focused element.
    /// </summary>
    public string? CurrentFocusKey { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the manager has active focusable targets.
    /// </summary>
    public bool HasFocusables
    {
        get
        {
#if NET9_0_OR_GREATER
            using (_sync.EnterScope())
#else
            lock (_sync)
#endif
            {
                return _focuseTargets.Count > 0;
            }
        }
    }

    /// <summary>
    /// Determines whether the supplied focus key corresponds to the active focus target.
    /// </summary>
    /// <param name="key">Focus key to compare.</param>
    /// <returns><see langword="true"/> when the key matches the active focus target; otherwise <see langword="false"/>.</returns>
    public bool IsFocused(string? key)
        => key is not null && string.Equals(CurrentFocusKey, key, StringComparison.Ordinal);

    /// <summary>
    /// Attempts to retrieve metadata associated with the currently focused target.
    /// </summary>
    /// <param name="snapshot">Populated with focus target details when available.</param>
    /// <returns><see langword="true"/> when a focus target is active; otherwise <see langword="false"/>.</returns>
    internal bool TryGetFocusedTarget(out FocusTarget? snapshot)
    {
#if NET9_0_OR_GREATER
        using (_sync.EnterScope())
#else
        lock (_sync)
#endif
        {
            if (_currentIndex < 0 || _currentIndex >= _focuseTargets.Count)
            {
                snapshot = default;
                return false;
            }

            snapshot = _focuseTargets[_currentIndex];
            return true;
        }
    }

    /// <summary>
    /// Begins a new focus session that tracks updates on the provided live display context.
    /// </summary>
    /// <param name="context">Live display context associated with the current console render.</param>
    /// <param name="initialView">The initial view rendered to the console.</param>
    /// <param name="shutdownToken">Token that signals when the session should be cancelled.</param>
    /// <returns>A disposable scope that tears down focus tracking when disposed.</returns>
    public FocusSession BeginSession(ConsoleLiveDisplayContext context, ConsoleViewResult initialView, CancellationToken shutdownToken)
    {
        if (context is null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (initialView is null)
        {
            throw new ArgumentNullException(nameof(initialView));
        }

        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);

        FocusTarget? initialFocus = null;

#if NET9_0_OR_GREATER
        using (_sync.EnterScope())
#else
        lock (_sync)
#endif
        {
            ResetState_NoLock();

            _context = context;
            _sessionCts = linkedCts;

            if (initialView.VdomRoot is not null)
            {
                var snapshot = new ConsoleRenderer.RenderSnapshot(
                    initialView.VdomRoot,
                    initialView.Renderable,
                    initialView.AnimatedRenderables);

                initialFocus = UpdateFocusTargets(snapshot);
            }
        }

        var initializationTask = initialFocus is not null
            ? TriggerFocusChangedAsync(null, initialFocus!, linkedCts.Token)
            : Task.CompletedTask;

        return new FocusSession(this, context, linkedCts, initializationTask);
    }

    /// <summary>
    /// Moves focus to the next focusable target in traversal order.
    /// </summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns><see langword="true"/> when focus changed; otherwise <see langword="false"/>.</returns>
    public async Task<bool> FocusNextAsync(CancellationToken token = default)
    {
        if (!TryMoveFocus(+1, out var previousTarget, out var nextTarget))
        {
            return false;
        }

        await TriggerFocusChangedAsync(previousTarget, nextTarget!, token).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Moves focus to the previous focusable target in traversal order.
    /// </summary>
    /// <param name="token">Cancellation token.</param>
    /// <returns><see langword="true"/> when focus changed; otherwise <see langword="false"/>.</returns>
    public async Task<bool> FocusPreviousAsync(CancellationToken token = default)
    {
        if (!TryMoveFocus(-1, out var previousTarget, out var nextTarget))
        {
            return false;
        }

        await TriggerFocusChangedAsync(previousTarget, nextTarget!, token).ConfigureAwait(false);
        return true;
    }

    private bool TryMoveFocus(int direction, out FocusTarget? previousTarget, out FocusTarget? nextTarget)
    {
        previousTarget = null;
        nextTarget = null;

#if NET9_0_OR_GREATER
        using (_sync.EnterScope())
#else
        lock (_sync)
#endif
        {
            if (_focuseTargets.Count == 0)
            {
                return false;
            }

            if (_currentIndex >= 0 && _currentIndex < _focuseTargets.Count)
            {
                previousTarget = _focuseTargets[_currentIndex];
            }

            int nextIndex;
            if (_currentIndex < 0)
            {
                nextIndex = direction > 0 ? 0 : _focuseTargets.Count - 1;
            }
            else
            {
                nextIndex = (_currentIndex + direction + _focuseTargets.Count) % _focuseTargets.Count;
            }

            _currentIndex = nextIndex;
            CurrentFocusKey = _focuseTargets[nextIndex].Key;
            nextTarget = _focuseTargets[nextIndex];
            return true;
        }
    }

    /// <summary>
    /// Attempts to focus the target that matches the supplied key.
    /// </summary>
    /// <param name="key">Focus key to activate.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns><see langword="true"/> when focus changed; otherwise <see langword="false"/>.</returns>
    public async Task<bool> FocusAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Focus key cannot be null or whitespace.", nameof(key));
        }

        FocusTarget? previousTarget = null;
        FocusTarget? target;

#if NET9_0_OR_GREATER
        using (_sync.EnterScope())
#else
        lock (_sync)
#endif
        {
            if (_focuseTargets.Count == 0)
            {
                return false;
            }

            var index = _focuseTargets.FindIndex(t => string.Equals(t.Key, key, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            if (_currentIndex == index)
            {
                return false;
            }

            if (_currentIndex >= 0 && _currentIndex < _focuseTargets.Count)
            {
                previousTarget = _focuseTargets[_currentIndex];
            }

            _currentIndex = index;
            CurrentFocusKey = _focuseTargets[index].Key;
            target = _focuseTargets[index];
        }

        await TriggerFocusChangedAsync(previousTarget, target!, token).ConfigureAwait(false);
        return true;
    }

    internal void EndSession(ConsoleLiveDisplayContext context)
    {
#if NET9_0_OR_GREATER
        using (_sync.EnterScope())
#else
        lock (_sync)
#endif
        {
            if (!ReferenceEquals(_context, context))
            {
                return;
            }

            ResetState_NoLock();
        }
    }

    private async Task TriggerFocusChangedAsync(FocusTarget? previousTarget, FocusTarget target, CancellationToken token)
    {
        if (token.IsCancellationRequested)
        {
            return;
        }

        FocusChanged?.Invoke(this, new FocusChangedEventArgs(target.Key));

        await DispatchFocusEventsAsync(previousTarget, target, token).ConfigureAwait(false);
    }

    private async Task DispatchFocusEventsAsync(FocusTarget? previousTarget, FocusTarget target, CancellationToken token)
    {
        var dispatcher = _eventDispatcher;
        if (dispatcher is null)
        {
            return;
        }

        token.ThrowIfCancellationRequested();

        if (previousTarget is not null && previousTarget.Events.TryGetEvent("onfocusout", out var focusOutEvent))
        {
            await dispatcher.DispatchAsync(focusOutEvent.HandlerId, new FocusEventArgs { Type = "focusout" }, token).ConfigureAwait(false);
        }

        if (target.Events.TryGetEvent("onfocusin", out var focusInEvent))
        {
            await dispatcher.DispatchAsync(focusInEvent.HandlerId, new FocusEventArgs { Type = "focusin" }, token).ConfigureAwait(false);
        }

        if (target.Events.TryGetEvent("onfocus", out var focusEvent))
        {
            await dispatcher.DispatchAsync(focusEvent.HandlerId, new FocusEventArgs { Type = "focus" }, token).ConfigureAwait(false);
        }
    }

    private void ResetState_NoLock()
    {
        _context = null;
        _sessionCts = null;
        _focuseTargets = new List<FocusTarget>();
        _currentIndex = -1;
        CurrentFocusKey = null;
    }

    private FocusTarget? UpdateFocusTargets(ConsoleRenderer.RenderSnapshot view)
    {
#if NET9_0_OR_GREATER
        using (_sync.EnterScope())
#else
        lock (_sync)
#endif
        {
            return UpdateFocusTargets_NoLock(view);
        }
    }

    private FocusTarget? UpdateFocusTargets_NoLock(ConsoleRenderer.RenderSnapshot view)
    {
        var targets = view.Root is null
            ? new List<FocusTarget>()
            : CollectTargets(view.Root);

        if (targets.Count == 0)
        {
            ResetState_NoLock();

            return null;
        }

        if (_currentIndex == -1)
        {
            _focuseTargets = targets;
            _currentIndex = 0;
            CurrentFocusKey = targets[0].Key;
            return targets[0];
        }

        var previousFocusTarget = _focuseTargets[_currentIndex];
        var matchIndex = targets.FindIndex(t => string.Equals(t.Key, previousFocusTarget.Key, StringComparison.Ordinal));
        _focuseTargets = targets;
        var currentFocusTarget = matchIndex >= 0
            ? _focuseTargets[matchIndex]
            : _focuseTargets[0];

        if (matchIndex >= 0)
        {
            _currentIndex = matchIndex;
            CurrentFocusKey = _focuseTargets[matchIndex].Key;
            return previousFocusTarget == currentFocusTarget ? null : currentFocusTarget;
        }

        _currentIndex = 0;
        CurrentFocusKey = _focuseTargets[0].Key;
        return _focuseTargets[0];
    }

    private static List<FocusTarget> CollectTargets(VNode root)
    {
        var targets = new List<FocusTarget>();
        var path = new List<int> { 0 };
        var sequence = 0;
        CollectRecursive(root, path, targets, ref sequence);
        return targets;
    }

    private static void CollectRecursive(VNode node, List<int> path, List<FocusTarget> targets, ref int sequence)
    {
        if (node.Kind == VNodeKind.Element && IsFocusable(node))
        {
            targets.Add(new FocusTarget(node, [.. path]));
        }

        var children = node.Children;
        for (var i = 0; i < children.Count; i++)
        {
            path.Add(i);
            CollectRecursive(children[i], path, targets, ref sequence);
            path.RemoveAt(path.Count - 1);
        }
    }

    private static bool IsFocusable(VNode element)
    {
        if (element.Kind != VNodeKind.Element)
        {
            return false;
        }

        if (!element.TryGetAttributeValue<string>("data-focusable", out var focusableValue) || string.IsNullOrWhiteSpace(focusableValue))
        {
            return element.Events.Count > 0;
        }

        if (bool.TryParse(focusableValue, out var focusable))
        {
            return focusable;
        }

        return element.Events.Count > 0;
    }

    private static string ResolveKey(VNode element, IReadOnlyList<int> path)
    {
        return element.Key ?? string.Join('.', path);
    }

    public void OnCompleted()
    {
    }

    public void OnError(Exception error)
    {
    }

    void IObserver<ConsoleRenderer.RenderSnapshot>.OnNext(ConsoleRenderer.RenderSnapshot value)
    {
#if NET9_0_OR_GREATER
        using (_sync.EnterScope())
#else
        lock (_sync)
#endif
        {
            FocusTarget? previousFocusTarget = _currentIndex >= 0 ? _focuseTargets[_currentIndex] : null;

            if (UpdateFocusTargets_NoLock(value) is not { } newFocus)
            {
                return;
            }

            if (previousFocusTarget?.Key != newFocus.Key)
            {
                TriggerFocusChangedAsync(previousFocusTarget, newFocus, _sessionCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                TriggerFocusChangedAsync(null, newFocus, _sessionCts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    internal sealed class FocusTarget : IEquatable<FocusTarget>
    {
        private readonly VNode _vnode;
        private readonly int[] _path;

        public FocusTarget(VNode vnode, int[] path)
        {
            _vnode = vnode;
            _path = path;

            Key = ResolveKey(_vnode, _path);
        }

        public string Key { get; }

        public IReadOnlyDictionary<string, object?> Attributes => _vnode.Attributes;

        public IReadOnlyCollection<VNodeEvent> Events => _vnode.Events;

        public VNode Node => _vnode;

        public bool Equals(FocusTarget? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return _vnode == other._vnode
                && Key == other.Key;
        }

        public override bool Equals(object? obj)
            => Equals(obj as FocusTarget);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(_vnode);

            foreach (var segment in _path)
            {
                hash.Add(segment);
            }

            return hash.ToHashCode();
        }

        public static bool operator ==(FocusTarget? left, FocusTarget? right)
        {
            if (left is null)
            {
                return right is null;
            }

            return left.Equals(right);
        }

        public static bool operator !=(FocusTarget? left, FocusTarget? right)
            => !(left == right);
    }


    /// <summary>
    /// Disposable scope representing an active focus tracking session.
    /// </summary>
    public sealed class FocusSession : IDisposable
    {
        private readonly FocusManager _manager;
        private readonly ConsoleLiveDisplayContext _context;
        private readonly CancellationTokenSource _cts;
        private readonly Task _initializationTask;
        private bool _disposed;

        internal FocusSession(FocusManager manager, ConsoleLiveDisplayContext context, CancellationTokenSource cts, Task initializationTask)
        {
            _manager = manager;
            _context = context;
            _cts = cts;
            _initializationTask = initializationTask ?? Task.CompletedTask;
        }

        /// <summary>
        /// Gets a token that is cancelled when the session ends.
        /// </summary>
        public CancellationToken Token => _cts.Token;

        /// <summary>
        /// Gets a task that completes once the initial focus state has been propagated.
        /// </summary>
        public Task InitializationTask => _initializationTask;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                _cts.Cancel();
            }
            catch
            {
                // Ignore cancellation exceptions during teardown.
            }
            finally
            {
                _cts.Dispose();
                _manager.EndSession(_context);
            }
        }
    }
}

/// <summary>
/// Event arguments emitted when focus switches between targets.
/// </summary>
public sealed class FocusChangedEventArgs : EventArgs
{
    internal FocusChangedEventArgs(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Gets the focus key associated with the active target.
    /// </summary>
    public string Key { get; }
}
