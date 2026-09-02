// Copyright (c) RazorConsole. All rights reserved.

#nullable enable
using System.Runtime.ExceptionServices;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using RazorConsole.Core.Extensions;

namespace RazorConsole.Core.Rendering;

/// <summary>
/// Serializes every entry into the Blazor <c>Renderer</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Renderer</c> is not thread-safe: it relies on its dispatcher to guarantee that
/// only one thread at a time builds a render batch, supplies parameters or applies a diff.
/// A console app has at least two threads that want in — the key-reading loop dispatching
/// events, and background work calling <c>InvokeAsync(StateHasChanged)</c> — so without
/// serialization the render tree corrupts (mismatched pool returns, expired
/// <c>ParameterView</c>s, then <c>NullReferenceException</c>s in the diff builder) and the
/// renderer stops producing frames.
/// </para>
/// <para>
/// Work items run on a single serial chain with the context installed, so awaits inside a
/// work item — including in fire-and-forget tasks started from an event handler — post
/// their continuations back onto the chain instead of resuming on a pool thread.
/// </para>
/// </remarks>
internal sealed class ConsoleDispatcher : Dispatcher
{
    private readonly ConsoleSynchronizationContext _context;

    public ConsoleDispatcher(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);

        var logger = loggerFactory.CreateLogger<ConsoleDispatcher>();

        // A work item that faults with nobody awaiting it would otherwise vanish, which is
        // exactly what makes render-loop problems hard to spot.
        _context = new ConsoleSynchronizationContext();
        _context.UnhandledException += ex => logger.LogErrorDuringRendering(ex);
    }

    public override bool CheckAccess() => ReferenceEquals(SynchronizationContext.Current, _context);

    public override Task InvokeAsync(Action workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        if (CheckAccess())
        {
            workItem();
            return Task.CompletedTask;
        }

        return PostAsync(() =>
        {
            workItem();
            return Task.CompletedTask;
        });
    }

    public override Task InvokeAsync(Func<Task> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return CheckAccess() ? workItem() : PostAsync(workItem);
    }

    public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        if (CheckAccess())
        {
            return Task.FromResult(workItem());
        }

        return PostAsync(() => Task.FromResult(workItem()));
    }

    public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return CheckAccess() ? workItem() : PostAsync(workItem);
    }

    private Task PostAsync(Func<Task> workItem)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _context.Post(
            async _ =>
            {
                try
                {
                    await workItem().ConfigureAwait(true);
                    completion.SetResult();
                }
                catch (OperationCanceledException ex)
                {
                    completion.TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            },
            state: null);

        return completion.Task;
    }

    private Task<TResult> PostAsync<TResult>(Func<Task<TResult>> workItem)
    {
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        _context.Post(
            async _ =>
            {
                try
                {
                    completion.SetResult(await workItem().ConfigureAwait(true));
                }
                catch (OperationCanceledException ex)
                {
                    completion.TrySetCanceled(ex.CancellationToken);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            },
            state: null);

        return completion.Task;
    }

    /// <summary>
    /// A <see cref="SynchronizationContext"/> that runs posted callbacks one at a time, in
    /// order, by chaining each onto the tail of a single task.
    /// </summary>
    private sealed class ConsoleSynchronizationContext : SynchronizationContext
    {
        private readonly Lock _sync = new();
        private Task _tail = Task.CompletedTask;

        public event Action<Exception>? UnhandledException;

        public override void Post(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);

            lock (_sync)
            {
                // Continue off the tail rather than starting fresh: that is what keeps
                // callbacks strictly serial. TaskScheduler.Default keeps them off whatever
                // thread happened to post, so no caller's stack is borrowed.
                _tail = _tail.ContinueWith(
                    _ => Execute(d, state),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default);
            }
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            ArgumentNullException.ThrowIfNull(d);

            if (ReferenceEquals(Current, this))
            {
                d(state);
                return;
            }

            using var completed = new ManualResetEventSlim(initialState: false);
            ExceptionDispatchInfo? failure = null;

            Post(
                _ =>
                {
                    try
                    {
                        d(state);
                    }
                    catch (Exception ex)
                    {
                        failure = ExceptionDispatchInfo.Capture(ex);
                    }
                    finally
                    {
                        completed.Set();
                    }
                },
                state: null);

            completed.Wait();
            failure?.Throw();
        }

        public override SynchronizationContext CreateCopy() => this;

        private void Execute(SendOrPostCallback callback, object? state)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                callback(state);
            }
            catch (Exception ex)
            {
                UnhandledException?.Invoke(ex);
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }
    }
}
