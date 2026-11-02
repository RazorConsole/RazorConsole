// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.AspNetCore.Components.Web;
using RazorConsole.Core.Controllers;
using RazorConsole.Core.Rendering;
using RazorConsole.Core.Vdom;

namespace RazorConsole.Core.Focus;

/// <summary>
/// Tracks focusable elements within the current virtual DOM and coordinates focus changes.
/// </summary>
public sealed class FocusManager : IObserver<ConsoleRenderer.RenderSnapshot>
{
    private readonly IFocusEventDispatcher? _eventDispatcher;
    private readonly Lock _sync = new();
    private List<FocusTarget> _focusTargets = new();
    private int _currentIndex = -1;
    private long _snapshotSequence;
    private string? _pendingFocusKey;
    private bool _hasPendingFocusRetry;
    private ConsoleLiveDisplayContext? _context;
    private TaskCompletionSource _snapshotSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static readonly TimeSpan StaleHandlerRetryTimeout = TimeSpan.FromMilliseconds(100);
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
            lock (_sync)
            {
                return _focusTargets.Count > 0;
            }
        }
    }

    /// <summary>
    /// Returns the focus targets in focus order, with the focused one marked, from one consistent moment.
    /// </summary>
    internal FocusTargetInfo[] GetFocusTargets()
    {
        lock (_sync)
        {
            var result = new FocusTargetInfo[_focusTargets.Count];
            for (var i = 0; i < result.Length; i++)
            {
                var target = _focusTargets[i];
                result[i] = new FocusTargetInfo(target.Key, target.VNodeId, i == _currentIndex);
            }

            return result;
        }
    }

    /// <summary>
    /// Gets the highest render snapshot sequence processed so far. It never goes down, and a session reset keeps it.
    /// </summary>
    internal long SnapshotSequence => Volatile.Read(ref _snapshotSequence);

    /// <summary>
    /// Waits until a snapshot with at least <paramref name="sequence"/> has been processed.
    /// Observers get snapshots asynchronously, so the targets can trail the newest render.
    /// </summary>
    /// <returns><see langword="true"/> when the manager caught up; <see langword="false"/> on timeout.</returns>
    internal async Task<bool> WaitForSnapshotAsync(long sequence, TimeSpan timeout, CancellationToken token)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeoutCts.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var next = Volatile.Read(ref _snapshotSignal).Task;
                if (SnapshotSequence >= sequence)
                {
                    return true;
                }

                await next.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            return SnapshotSequence >= sequence;
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
        lock (_sync)
        {
            if (_currentIndex < 0 || _currentIndex >= _focusTargets.Count)
            {
                snapshot = default;
                return false;
            }

            snapshot = _focusTargets[_currentIndex];
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

        lock (_sync)
        {
            // Components can request focus while mounting, before the session starts; keep that request.
            var requestedKey = _hasPendingFocusRetry ? _pendingFocusKey : CurrentFocusKey;
            var requestPending = _hasPendingFocusRetry;

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

            if (requestedKey is not null)
            {
                var requestedIndex = _focusTargets.FindIndex(t => string.Equals(t.Key, requestedKey, StringComparison.Ordinal));
                if (requestedIndex >= 0)
                {
                    _currentIndex = requestedIndex;
                    CurrentFocusKey = requestedKey;
                    initialFocus = _focusTargets[requestedIndex];
                }
                else if (requestPending)
                {
                    _pendingFocusKey = requestedKey;
                    _hasPendingFocusRetry = true;
                }
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

        lock (_sync)
        {
            if (_focusTargets.Count == 0)
            {
                return false;
            }

            // Explicit navigation wins over a request still waiting for its target to render.
            _pendingFocusKey = null;
            _hasPendingFocusRetry = false;

            if (_currentIndex >= 0 && _currentIndex < _focusTargets.Count)
            {
                previousTarget = _focusTargets[_currentIndex];
            }

            int nextIndex;
            if (_currentIndex < 0)
            {
                nextIndex = direction > 0 ? 0 : _focusTargets.Count - 1;
            }
            else
            {
                nextIndex = (_currentIndex + direction + _focusTargets.Count) % _focusTargets.Count;
            }

            _currentIndex = nextIndex;
            CurrentFocusKey = _focusTargets[nextIndex].Key;
            nextTarget = _focusTargets[nextIndex];
            return true;
        }
    }

    /// <summary>
    /// Attempts to focus the target that matches the supplied key.
    /// If the target does not exist in the current snapshot, the request is stored and applied
    /// when a later snapshot contains it, unless focus is moved explicitly first.
    /// </summary>
    /// <param name="key">Focus key to activate.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>
    /// <see langword="true"/> when focus changes immediately; otherwise <see langword="false"/>.
    /// A <see langword="false"/> result can indicate the key was queued until its target renders.
    /// </returns>
    public async Task<bool> FocusAsync(string key, CancellationToken token = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Focus key cannot be null or whitespace.", nameof(key));
        }

        FocusTarget? previousTarget = null;
        FocusTarget? target;

        lock (_sync)
        {
            if (_focusTargets.Count == 0)
            {
                _pendingFocusKey = key;
                _hasPendingFocusRetry = true;
                return false;
            }

            var index = _focusTargets.FindIndex(t => string.Equals(t.Key, key, StringComparison.Ordinal));
            if (index < 0)
            {
                _pendingFocusKey = key;
                _hasPendingFocusRetry = true;
                return false;
            }

            if (_currentIndex == index)
            {
                _pendingFocusKey = null;
                _hasPendingFocusRetry = false;
                return false;
            }

            if (_currentIndex >= 0 && _currentIndex < _focusTargets.Count)
            {
                previousTarget = _focusTargets[_currentIndex];
            }

            _currentIndex = index;
            CurrentFocusKey = _focusTargets[index].Key;
            _pendingFocusKey = null;
            _hasPendingFocusRetry = false;
            target = _focusTargets[index];
        }

        await TriggerFocusChangedAsync(previousTarget, target!, token).ConfigureAwait(false);
        return true;
    }

    internal void EndSession(ConsoleLiveDisplayContext context)
    {
        lock (_sync)
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

        if (previousTarget is not null)
        {
            await DispatchToLatestAsync(dispatcher, previousTarget, "onfocusout", "focusout", focused: false, token).ConfigureAwait(false);
        }

        await DispatchToLatestAsync(dispatcher, target, "onfocusin", "focusin", focused: true, token).ConfigureAwait(false);
        await DispatchToLatestAsync(dispatcher, target, "onfocus", "focus", focused: true, token).ConfigureAwait(false);
    }

    // A target that is gone (e.g. after navigation) is skipped, and so is an event that focus has
    // since overtaken: a late "focus" would mark an element focused after focus moved on.
    private async Task DispatchToLatestAsync(IFocusEventDispatcher dispatcher, FocusTarget target, string eventName, string eventType, bool focused, CancellationToken token)
    {
        try
        {
            await DispatchToLatestRenderAsync(
                target,
                eventName,
                handlerId => IsCurrentFocus(target) == focused
                    ? dispatcher.DispatchAsync(handlerId, new FocusEventArgs { Type = eventType }, token)
                    : Task.CompletedTask,
                StaleHandlerRetryTimeout,
                token).ConfigureAwait(false);
        }
        catch (ArgumentException ex) when (ex.ParamName == "eventHandlerId")
        {
            // Still stale after the next snapshot: the target is gone.
        }
    }

    /// <summary>
    /// Dispatches <paramref name="eventName"/> to the newest known render of <paramref name="target"/>.
    /// A target captured from one snapshot may have re-rendered since, which replaces its handler ids:
    /// on a stale id, waits for the next snapshot (at most <paramref name="staleRetryTimeout"/>) and retries once.
    /// </summary>
    /// <returns><see langword="false"/> when the target has no handler for the event.</returns>
    internal async Task<bool> DispatchToLatestRenderAsync(FocusTarget target, string eventName, Func<ulong, Task> dispatch, TimeSpan staleRetryTimeout, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            var nextSnapshot = Volatile.Read(ref _snapshotSignal).Task;
            if (!LatestRender(target).Events.TryGetEvent(eventName, out var nodeEvent))
            {
                return false;
            }

            try
            {
                await dispatch(nodeEvent.HandlerId).ConfigureAwait(false);
                return true;
            }
            catch (ArgumentException ex) when (ex.ParamName == "eventHandlerId" && attempt == 0)
            {
                await Task.WhenAny(nextSnapshot, Task.Delay(staleRetryTimeout, token)).ConfigureAwait(false);
            }
        }
    }

    private bool IsCurrentFocus(FocusTarget target)
    {
        lock (_sync)
        {
            return string.Equals(CurrentFocusKey, target.Key, StringComparison.Ordinal);
        }
    }

    private FocusTarget LatestRender(FocusTarget target)
    {
        target.Attributes.TryGetValue("data-focus-key", out var focusKey);
        lock (_sync)
        {
            return _focusTargets.FirstOrDefault(t =>
                string.Equals(t.Key, target.Key, StringComparison.Ordinal)
                && string.Equals(t.Attributes.GetValueOrDefault("data-focus-key"), focusKey, StringComparison.Ordinal))
                ?? target;
        }
    }

    private void ResetState_NoLock()
    {
        _context = null;
        _sessionCts = null;
        _focusTargets = new List<FocusTarget>();
        _currentIndex = -1;
        _pendingFocusKey = null;
        _hasPendingFocusRetry = false;
        CurrentFocusKey = null;
    }

    private FocusTarget? UpdateFocusTargets(ConsoleRenderer.RenderSnapshot view)
    {
        lock (_sync)
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
            _focusTargets = targets;
            _currentIndex = 0;
            CurrentFocusKey = targets[0].Key;
            return targets[0];
        }

        var previousFocusTarget = _focusTargets[_currentIndex];

        // Try to find a matching target by both Key (path) AND data-focus-key (component instance GUID).
        // This ensures that when navigating between pages, we don't accidentally focus a component
        // at the same position but with a different identity.
        previousFocusTarget.Attributes.TryGetValue("data-focus-key", out var previousFocusKey);
        var matchIndex = targets.FindIndex(t =>
        {
            if (!string.Equals(t.Key, previousFocusTarget.Key, StringComparison.Ordinal))
            {
                return false;
            }

            // If the previous target had a data-focus-key, the new target must have the same one
            if (previousFocusKey is not null)
            {
                t.Attributes.TryGetValue("data-focus-key", out var newFocusKey);
                return string.Equals(previousFocusKey, newFocusKey, StringComparison.Ordinal);
            }

            return true;
        });

        _focusTargets = targets;
        var currentFocusTarget = matchIndex >= 0
            ? _focusTargets[matchIndex]
            : _focusTargets[0];

        if (matchIndex >= 0)
        {
            _currentIndex = matchIndex;
            CurrentFocusKey = _focusTargets[matchIndex].Key;
            return previousFocusTarget == currentFocusTarget ? null : currentFocusTarget;
        }

        _currentIndex = 0;
        CurrentFocusKey = _focusTargets[0].Key;
        return _focusTargets[0];
    }

    private static List<FocusTarget> CollectTargets(VNode root)
    {
        var targets = new List<FocusTarget>();
        var path = new List<int> { 0 };
        var sequence = 0;
        CollectRecursive(root, path, targets, ref sequence);

        // Sort by focus order if specified, maintaining DOM order for elements with same/no order
        targets = targets
            .Select(static (target, index) => new
            {
                Target = target,
                OriginalIndex = index,
                FocusOrder = target.Attributes.TryGetValue("data-focus-order", out var orderStr) &&
                             int.TryParse(orderStr, out var order)
                    ? order
                    : int.MaxValue // Elements without focus order come last
            })
            .OrderBy(static item => item.FocusOrder)
            .ThenBy(static item => item.OriginalIndex) // Maintain DOM order as tiebreaker
            .Select(static item => item.Target)
            .ToList();

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

        if (!element.Attributes.TryGetValue("data-focusable", out var focusableValue) || string.IsNullOrWhiteSpace(focusableValue))
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
        FocusTarget? previousFocusTarget;
        FocusTarget? newFocus;
        FocusTarget? pendingFocusTarget = null;
        CancellationToken sessionToken;

        lock (_sync)
        {
            previousFocusTarget = _currentIndex >= 0 ? _focusTargets[_currentIndex] : null;
            sessionToken = _sessionCts?.Token ?? CancellationToken.None;

            newFocus = UpdateFocusTargets_NoLock(value);
            if (value.Sequence > _snapshotSequence)
            {
                Volatile.Write(ref _snapshotSequence, value.Sequence);
            }

            if (_hasPendingFocusRetry && !string.IsNullOrWhiteSpace(_pendingFocusKey))
            {
                var pendingKey = _pendingFocusKey;
                var pendingIndex = _focusTargets.FindIndex(t => string.Equals(t.Key, pendingKey, StringComparison.Ordinal));
                if (pendingIndex >= 0 && pendingIndex != _currentIndex)
                {
                    previousFocusTarget = _currentIndex >= 0 && _currentIndex < _focusTargets.Count
                        ? _focusTargets[_currentIndex]
                        : null;
                    _currentIndex = pendingIndex;
                    CurrentFocusKey = _focusTargets[pendingIndex].Key;
                    pendingFocusTarget = _focusTargets[pendingIndex];
                    _pendingFocusKey = null;
                    _hasPendingFocusRetry = false;
                }
                else if (pendingIndex >= 0)
                {
                    _pendingFocusKey = null;
                    _hasPendingFocusRetry = false;
                }
            }
        }

        Interlocked.Exchange(ref _snapshotSignal, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();

        if (pendingFocusTarget is not null)
        {
            if (previousFocusTarget?.Key != pendingFocusTarget.Key)
            {
                TriggerFocusChangedAsync(previousFocusTarget, pendingFocusTarget, sessionToken).ConfigureAwait(false);
            }
            else
            {
                TriggerFocusChangedAsync(null, pendingFocusTarget, sessionToken).ConfigureAwait(false);
            }

            return;
        }

        if (newFocus is null)
        {
            return;
        }

        if (previousFocusTarget?.Key != newFocus.Key)
        {
            TriggerFocusChangedAsync(previousFocusTarget, newFocus, sessionToken).ConfigureAwait(false);
        }
        else
        {
            TriggerFocusChangedAsync(null, newFocus, sessionToken).ConfigureAwait(false);
        }
    }

    internal readonly record struct FocusTargetInfo(string Key, string VNodeId, bool IsFocused);

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

        public string VNodeId => _vnode.ID;

        public IReadOnlyDictionary<string, string?> Attributes => _vnode.Attributes;

        public IReadOnlyCollection<VNodeEvent> Events => _vnode.Events;

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
