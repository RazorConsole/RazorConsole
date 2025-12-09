// Copyright (c) RazorConsole. All rights reserved.

#nullable enable
#pragma warning disable BL0006 // RenderTree types are "internal-ish"; acceptable for console renderer.
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging;
using RazorConsole.Core.Extensions;
using RazorConsole.Core.Rendering.ComponentMarkup;
using RazorConsole.Core.Vdom;
using Spectre.Console.Rendering;

namespace RazorConsole.Core.Rendering;

internal sealed class ConsoleRenderer(
    IServiceProvider services,
    ILoggerFactory loggerFactory,
    Translation.Contexts.TranslationContext translationContext)
    : Renderer(services, loggerFactory),
    IObservable<ConsoleRenderer.RenderSnapshot>
{
    private sealed class ImmediateDispatcher : Dispatcher
    {
        public override bool CheckAccess() => true;

        public override Task InvokeAsync(Action workItem)
        {
            workItem?.Invoke();
            return Task.CompletedTask;
        }

        public override Task InvokeAsync(Func<Task> workItem)
            => workItem?.Invoke() ?? Task.CompletedTask;

        public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
        {
            if (workItem is null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }

            return Task.FromResult(workItem());
        }

        public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
        {
            if (workItem is null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }

            return workItem();
        }
    }

    private static readonly ImmediateDispatcher DispatcherInstance = new();

    private readonly Dictionary<int, VNode> _componentRoots = [];
    private readonly Stack<VNode> _cursor = new();
    private readonly ILogger<ConsoleRenderer> _logger = loggerFactory?.CreateLogger<ConsoleRenderer>()
        ?? throw new ArgumentNullException(nameof(loggerFactory));
    private readonly Translation.Contexts.TranslationContext _translationContext = translationContext;
#if NET9_0_OR_GREATER
    private readonly Lock _observersSync = new();
#else
    private readonly object _observersSync = new();
#endif
    private readonly List<IObserver<RenderSnapshot>> _observers = [];

    private TaskCompletionSource<RenderSnapshot>? _pendingRender;
    private int _rootComponentId = -1;
    private RenderSnapshot _lastSnapshot = RenderSnapshot.Empty;
    private bool _disposed;

    public override Dispatcher Dispatcher => DispatcherInstance;

    internal Translation.Contexts.TranslationContext GetTranslationContext() => _translationContext;

    public Task<RenderSnapshot> MountComponentAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(ParameterView parameters, CancellationToken cancellationToken)
        where TComponent : IComponent
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        cancellationToken.ThrowIfCancellationRequested();

        TComponent component = (TComponent)InstantiateComponent(typeof(TComponent));

        return MountComponentAsync(component, parameters, cancellationToken);
    }

    internal async Task<RenderSnapshot> MountComponentAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(TComponent component, ParameterView parameters, CancellationToken cancellationToken)
        where TComponent : IComponent
    {
        var componentId = AssignRootComponentId(component);
        _rootComponentId = componentId;

        var tcs = new TaskCompletionSource<RenderSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        _pendingRender = tcs;

        try
        {
            await RenderRootComponentAsync(componentId, parameters).ConfigureAwait(false);
            _lastSnapshot = await tcs.Task.ConfigureAwait(false);
            return _lastSnapshot;
        }
        catch (OperationCanceledException)
        {
            _logger.LogComponentMountingCancelled();
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogErrorMountingComponent(ex, typeof(TComponent).Name);
            _pendingRender?.TrySetException(ex);
            throw;
        }
        finally
        {
            _pendingRender = null;
        }
    }

    public IDisposable Subscribe(IObserver<RenderSnapshot> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);

        RenderSnapshot snapshot;
        IDisposable subscription;
        lock (_observersSync)
        {
            if (_disposed)
            {
                snapshot = _lastSnapshot;
                subscription = NoopDisposable.Instance;
            }
            else
            {
                _observers.Add(observer);
                snapshot = _lastSnapshot;
                subscription = new Subscription(this, observer);
            }
        }

        observer.OnNext(snapshot);

        if (_disposed)
        {
            observer.OnCompleted();
        }

        return subscription;
    }

    protected override Task UpdateDisplayAsync(in RenderBatch batch)
    {
        try
        {
            // Process updated components
            var updatedComponents = batch.UpdatedComponents;
            var updatedComponentsArray = updatedComponents.Array;
            var updatedComponentsCount = updatedComponents.Count;
            for (var i = 0; i < updatedComponentsCount; i++)
            {
                var diff = updatedComponentsArray[i];
                try
                {
                    ApplyComponentEdits(batch, diff);
                }
                catch (Exception ex)
                {
                    _logger.LogErrorApplyingComponentEdits(ex, diff.ComponentId);
                    throw;
                }
            }

            // Process disposed components
            var disposedComponentIds = batch.DisposedComponentIDs;
            var disposedComponentIdsArray = disposedComponentIds.Array;
            var disposedComponentIdsCount = disposedComponentIds.Count;
            for (var i = 0; i < disposedComponentIdsCount; i++)
            {
                var componentId = disposedComponentIdsArray[i];
                _componentRoots.Remove(componentId);
            }

            var snapshot = CreateSnapshot();
            _lastSnapshot = snapshot;
            _pendingRender?.TrySetResult(snapshot);
            _pendingRender = null;

            // Notify observers asynchronously with proper error handling
            _ = Task.Run(() =>
            {
                try
                {
                    NotifyObservers(snapshot);
                }
                catch (Exception ex)
                {
                    _logger.LogErrorNotifyingObserverOfSnapshot(ex);
                }
            });

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogErrorInUpdateDisplayAsync(ex);
            _pendingRender?.TrySetException(ex);
            _pendingRender = null;
            throw;
        }
    }

    protected override void HandleException(Exception exception)
    {
        _logger.LogErrorDuringRendering(exception);
        NotifyError(exception);
        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception).Throw();
    }

    private void ApplyComponentEdits(in RenderBatch batch, RenderTreeDiff diff)
    {
        var slot = GetOrCreateComponentSlot(diff.ComponentId);
        ResetCursor(slot);
        var edits = diff.Edits;
        var offset = edits.Offset;
        for (var i = 0; i < edits.Count; i++)
        {
            var edit = edits.Array[i + offset];
            switch (edit.Type)
            {
                case RenderTreeEditType.PrependFrame:
                    ApplyPrependFrameEdit(batch, edit);
                    break;
                case RenderTreeEditType.RemoveFrame:
                    ApplyRemoveFrameEdit(edit);
                    break;
                case RenderTreeEditType.SetAttribute:
                    ApplySetAttributeEdit(batch, edit);
                    break;
                case RenderTreeEditType.RemoveAttribute:
                    ApplyRemoveAttributeEdit(batch, edit);
                    break;
                case RenderTreeEditType.UpdateText:
                    ApplyUpdateTextEdit(batch, edit);
                    break;
                case RenderTreeEditType.StepIn:
                    ApplyStepInEdit(edit);
                    break;
                case RenderTreeEditType.StepOut:
                    ApplyStepOutEdit();
                    break;
            }
        }
    }

    private void ApplyPrependFrameEdit(in RenderBatch batch, RenderTreeEdit edit)
    {
        var parent = _cursor.Peek();
        var referenceFrame = batch.ReferenceFrames.Array[edit.ReferenceFrameIndex];
        if (referenceFrame.FrameType == RenderTreeFrameType.Attribute)
        {
            ApplyAttributeFrame(parent, referenceFrame);
            return;
        }

        var (child, _) = BuildSubtree(batch.ReferenceFrames, edit.ReferenceFrameIndex);
        parent.InsertChild(edit.SiblingIndex, child);
    }

    private void ApplyRemoveFrameEdit(RenderTreeEdit edit)
    {
        var parent = _cursor.Peek();
        parent.RemoveChildAt(edit.SiblingIndex);
    }

    private void ApplySetAttributeEdit(in RenderBatch batch, RenderTreeEdit edit)
    {
        var parent = _cursor.Peek();
        var frame = batch.ReferenceFrames.Array[edit.ReferenceFrameIndex];
        Debug.Assert(frame.FrameType == RenderTreeFrameType.Attribute, "SetAttribute edit must reference an Attribute frame.");
        var child = BFSNonRegionChildren(parent).ElementAt(edit.SiblingIndex);
        ApplyAttributeFrame(child, frame);
    }

    private void ApplyRemoveAttributeEdit(in RenderBatch batch, RenderTreeEdit edit)
    {
        var parent = _cursor.Peek();
        var frame = batch.ReferenceFrames.Array[edit.ReferenceFrameIndex];
        Debug.Assert(frame.FrameType == RenderTreeFrameType.Attribute, "RemoveAttribute edit must reference an Attribute frame.");
        var child = BFSNonRegionChildren(parent).ElementAt(edit.SiblingIndex);
        if (frame.AttributeEventHandlerId != 0)
        {
            child.RemoveEvent(frame.AttributeName!);
        }
        else
        {
            child.RemoveAttribute(frame.AttributeName!);
            if (IsKeyAttribute(frame.AttributeName!))
            {
                parent.SetKey(null);
            }
        }
    }

    private void ApplyUpdateTextEdit(in RenderBatch batch, RenderTreeEdit edit)
    {
        var parent = _cursor.Peek();
        var child = BFSNonRegionChildren(parent).ElementAt(edit.SiblingIndex);
        var frame = batch.ReferenceFrames.Array[edit.ReferenceFrameIndex];
        var textContent = frame.FrameType == RenderTreeFrameType.Text
            ? frame.TextContent
            : frame.MarkupContent;
        child.SetText(textContent);
    }

    private void ApplyStepInEdit(RenderTreeEdit edit)
    {
        var vnode = _cursor.Peek();
        var child = BFSNonRegionChildren(vnode).ElementAt(edit.SiblingIndex);
        _cursor.Push(child);

        return;
    }

    private void ApplyStepOutEdit()
    {
        Debug.Assert(_cursor.Count > 0, "Cursor should not be empty after applying edits.");
        _cursor.Pop();
        return;
    }

    private (VNode Node, int NextIndex) BuildSubtree(ArrayRange<RenderTreeFrame> frames, int index)
    {
        var frame = frames.Array[index];
        switch (frame.FrameType)
        {
            case RenderTreeFrameType.Element:
            {
                var element = VNode.CreateElement(frame.ElementName!);
                element.SetKey(frame.ElementKey?.ToString());
                var end = index + frame.ElementSubtreeLength;
                index++;

                while (index < end && frames.Array[index].FrameType == RenderTreeFrameType.Attribute)
                {
                    var attribute = frames.Array[index];
                    if (attribute.AttributeEventHandlerId != 0)
                    {
                        element.SetEvent(attribute.AttributeName!, attribute.AttributeEventHandlerId);
                    }
                    else
                    {
                        var value = FormatAttributeValue(attribute.AttributeValue);
                        element.SetAttribute(attribute.AttributeName!, value);
                    }

                    index++;
                }

                while (index < end)
                {
                    var (child, next) = BuildSubtree(frames, index);
                    element.AddChild(child);
                    index = next;
                }

                return (element, index);
            }

            case RenderTreeFrameType.Text:
                return (VNode.CreateText(frame.TextContent), index + 1);

            case RenderTreeFrameType.Markup:
                if (HtmlVdomConverter.TryConvert(frame.MarkupContent, out var vnode) && vnode is not null)
                {
                    return (vnode, index + 1);
                }
                else
                {
                    return (VNode.CreateText(frame.MarkupContent), index + 1);
                }

            case RenderTreeFrameType.Region:
            {
                var region = VNode.CreateRegion();
                var end = index + frame.RegionSubtreeLength;
                index++;

                while (index < end)
                {
                    var (child, next) = BuildSubtree(frames, index);
                    region.AddChild(child);
                    index = next;
                }

                return (region, index);
            }

            case RenderTreeFrameType.Component:
            {
                var componentId = frame.ComponentId;
                var component = VNode.CreateComponent();
                component.SetAttribute("component-id", componentId.ToString(CultureInfo.InvariantCulture));
                if (frame.ComponentType is not null)
                {
                    component.SetAttribute("component-type", frame.ComponentType.FullName);
                }

                var end = index + frame.ComponentSubtreeLength;
                index++;

                while (index < end && frames.Array[index].FrameType == RenderTreeFrameType.Attribute)
                {
                    var attribute = frames.Array[index];
                    if (attribute.AttributeEventHandlerId == 0)
                    {
                        var value = FormatAttributeValue(attribute.AttributeValue);
                        component.SetAttribute(attribute.AttributeName!, value);
                    }

                    index++;
                }

                index = end;
                return (component, index);
            }

            default:
                return (VNode.CreateRegion(), index + 1);
        }
    }

    /// <summary>
    /// BFS traversal to yield all non-region first-level children of a VNode. Grandchildren of none-region children are not included.
    /// </summary>
    private IEnumerable<VNode> BFSNonRegionChildren(VNode node)
    {
        foreach (var child in node.Children)
        {
            if (child.Kind != VNodeKind.Region)
            {
                yield return child;
            }
            else
            {
                foreach (var grandchild in BFSNonRegionChildren(child))
                {
                    yield return grandchild;
                }
            }
        }
    }

    private void ResetCursor(VNode root)
    {
        _cursor.Clear();
        _cursor.Push(root);
    }

    private VNode GetOrCreateComponentSlot(int componentId)
    {
        if (_componentRoots.TryGetValue(componentId, out var node))
        {
            return node;
        }

        var slot = VNode.CreateComponent();
        slot.SetAttribute("component-id", componentId.ToString(CultureInfo.InvariantCulture));
        _componentRoots[componentId] = slot;
        return slot;
    }

    private RenderSnapshot CreateSnapshot()
    {
        try
        {
            if (_rootComponentId == -1 || !_componentRoots.TryGetValue(_rootComponentId, out var componentNode))
            {
                return RenderSnapshot.Empty;
            }

            var vnode = CreateRenderableRoot(componentNode);
            if (vnode is null)
            {
                return RenderSnapshot.Empty;
            }

            _translationContext.AnimatedRenderables.Clear();
            var renderable = _translationContext.Translate(vnode);
            return new RenderSnapshot(vnode, renderable, _translationContext.AnimatedRenderables);
        }
        catch (Exception ex)
        {
            _logger.LogErrorCreatingRenderSnapshot(ex);
            return RenderSnapshot.Empty;
        }
    }

    private VNode? CreateRenderableRoot(VNode node)
    {
        var visitedComponents = new HashSet<int>();
        var nodeId = TryGetComponentId(node);
        if (nodeId.HasValue)
        {
            visitedComponents.Add(nodeId.Value);
        }

        var collected = CollectRenderableChildren(node, visitedComponents);
        return collected.Count switch
        {
            0 => null,
            1 => collected[0],
            _ => CreateDivWrapper(collected),
        };
    }

    private List<VNode> CollectRenderableChildren(VNode node, HashSet<int> visitedComponents)
    {
        var result = new List<VNode>();
        foreach (var child in node.Children)
        {
            EnumerateRenderableSubtree(child, visitedComponents, result);
        }

        return result;
    }

    private void EnumerateRenderableSubtree(VNode node, HashSet<int> visitedComponents, List<VNode> result)
    {
        switch (node.Kind)
        {
            case VNodeKind.Element:
                result.Add(CloneElementWithRenderableChildren(node, visitedComponents));
                break;
            case VNodeKind.Text:
                result.Add(VNode.CreateText(node.Text));
                break;
            case VNodeKind.Component:
                EnumerateComponentRenderableChildren(node, visitedComponents, result);
                break;
            default:
                foreach (var child in node.Children)
                {
                    EnumerateRenderableSubtree(child, visitedComponents, result);
                }
                break;
        }
    }

    private void EnumerateComponentRenderableChildren(VNode node, HashSet<int> visitedComponents, List<VNode> result)
    {
        var childId = TryGetComponentId(node);
        if (childId.HasValue && !visitedComponents.Contains(childId.Value) && _componentRoots.TryGetValue(childId.Value, out var componentRoot))
        {
            visitedComponents.Add(childId.Value);
            var descendants = CollectRenderableChildren(componentRoot, visitedComponents);
            result.AddRange(descendants);
            visitedComponents.Remove(childId.Value);
            return;
        }

        foreach (var child in node.Children)
        {
            EnumerateRenderableSubtree(child, visitedComponents, result);
        }
    }

    private VNode CloneElementWithRenderableChildren(VNode element, HashSet<int> visitedComponents)
    {
        var tagName = element.TagName;
        if (string.IsNullOrWhiteSpace(tagName))
        {
            _logger.LogElementVNodeMissingTagName();
            throw new InvalidOperationException("Element VNodes must define a tag name.");
        }

        var clone = VNode.CreateElement(tagName);
        if (!string.IsNullOrWhiteSpace(element.Key))
        {
            clone.SetKey(element.Key);
        }

        foreach (var attribute in element.Attributes)
        {
            clone.SetAttribute(attribute.Key, attribute.Value);
        }

        foreach (var @event in element.Events)
        {
            clone.SetEvent(@event.Name, @event.HandlerId, @event.Options);
        }

        var nestedChildren = new List<VNode>();
        foreach (var child in element.Children)
        {
            EnumerateRenderableSubtree(child, visitedComponents, nestedChildren);
        }

        foreach (var nested in nestedChildren)
        {
            clone.AddChild(nested);
        }

        return clone;
    }

    private static VNode CreateDivWrapper(List<VNode> children)
    {
        var wrapper = VNode.CreateElement("div");
        foreach (var child in children)
        {
            wrapper.AddChild(child);
        }

        return wrapper;
    }

    private static int? TryGetComponentId(VNode node)
    {
        if (node.Attributes.TryGetValue("component-id", out var value)
            && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var componentId))
        {
            return componentId;
        }

        return null;
    }

    private static void ApplyAttributeFrame(VNode parent, RenderTreeFrame frame)
    {
        if (frame.AttributeEventHandlerId != 0)
        {
            parent.SetEvent(frame.AttributeName!, frame.AttributeEventHandlerId);
            return;
        }

        var value = FormatAttributeValue(frame.AttributeValue);
        parent.SetAttribute(frame.AttributeName!, value);
        if (IsKeyAttribute(frame.AttributeName!))
        {
            parent.SetKey(string.IsNullOrWhiteSpace(value) ? null : value);
        }
    }

    private static bool IsKeyAttribute(string name)
        => string.Equals(name, "key", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "data-key", StringComparison.OrdinalIgnoreCase);

    private static readonly string TrueString = "true";
    private static readonly string FalseString = "false";

    private static string? FormatAttributeValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is string s)
        {
            return s;
        }

        if (value is bool b)
        {
            return b ? TrueString : FalseString;
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? string.Empty;
    }

    public readonly record struct RenderSnapshot(VNode? Root, IRenderable? Renderable, IReadOnlyCollection<IAnimatedConsoleRenderable> AnimatedRenderables)
    {
        public static RenderSnapshot Empty { get; } = new(null, null, Array.Empty<IAnimatedConsoleRenderable>());
    }

    private void NotifyObservers(RenderSnapshot snapshot)
    {
        NotifyObserversInternal(observer => observer.OnNext(snapshot), ex => _logger.LogErrorNotifyingObserverOfSnapshot(ex));
    }

    private void NotifyError(Exception exception)
    {
        NotifyObserversInternal(observer => observer.OnError(exception), ex => _logger.LogErrorNotifyingObserverOfError(ex));
    }

    private void NotifyObserversInternal(Action<IObserver<RenderSnapshot>> action, Action<Exception> errorLogger)
    {
        IObserver<RenderSnapshot>[] observers;
        lock (_observersSync)
        {
            if (_observers.Count == 0)
            {
                return;
            }

            observers = [.. _observers];
        }

        foreach (var observer in observers)
        {
            try
            {
                action(observer);
            }
            catch (Exception ex)
            {
                errorLogger(ex);
            }
        }
    }

    internal Task DispatchEventAsync(ulong handlerId, EventArgs eventArgs)
    {
        if (handlerId == 0)
        {
            _logger.LogInvalidHandlerId();
            throw new ArgumentOutOfRangeException(nameof(handlerId));
        }

        if (eventArgs is null)
        {
            _logger.LogNullEventArgs();
            throw new ArgumentNullException(nameof(eventArgs));
        }

        try
        {
            return base.DispatchEventAsync(handlerId, default, eventArgs);
        }
        catch (Exception ex)
        {
            _logger.LogErrorDispatchingEvent(ex, handlerId);
            throw;
        }
    }

    private void CompleteObservers()
    {
        IObserver<RenderSnapshot>[] observers;
        lock (_observersSync)
        {
            if (_observers.Count == 0)
            {
                return;
            }

            observers = [.. _observers];
            _observers.Clear();
        }

        foreach (var observer in observers)
        {
            try
            {
                observer.OnCompleted();
            }
            catch (Exception ex)
            {
                _logger.LogErrorCompletingObserver(ex);
            }
        }
    }

    private void Unsubscribe(IObserver<RenderSnapshot> observer)
    {
        lock (_observersSync)
        {
            _observers.Remove(observer);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_observersSync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            try
            {
                CompleteObservers();
            }
            catch (Exception ex)
            {
                _logger.LogErrorCompletingObserversDuringDispose(ex);
            }

            _componentRoots.Clear();
            _cursor.Clear();
            _pendingRender?.TrySetCanceled();
            _pendingRender = null;
        }

        base.Dispose(disposing);
    }

    private sealed class Subscription(
        ConsoleRenderer owner,
        IObserver<ConsoleRenderer.RenderSnapshot> observer)
        : IDisposable
    {
        private IObserver<RenderSnapshot>? _observer = observer;

        public void Dispose()
        {
            var observer = Interlocked.Exchange(ref _observer, null);
            if (observer is not null)
            {
                owner.Unsubscribe(observer);
            }
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();

        private NoopDisposable()
        {
        }

        public void Dispose()
        {
        }
    }
}
#pragma warning restore BL0006
