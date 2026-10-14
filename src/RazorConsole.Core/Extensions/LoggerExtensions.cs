// Copyright (c) RazorConsole. All rights reserved.

using Microsoft.Extensions.Logging;

namespace RazorConsole.Core.Extensions;

internal static partial class LoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Debug,
        Message = "Transient failure while reading keyboard input.")]
    public static partial void LogTransientKeyboardInputFailure(
        this ILogger logger,
        Exception ex);

    [LoggerMessage(
        EventId = 2,
        Level = LogLevel.Debug,
        Message = "Unable to update focus target.")]
    public static partial void LogUnableToUpdateFocusTarget(
        this ILogger logger,
        Exception ex);

    [LoggerMessage(
        EventId = 3,
        Level = LogLevel.Error,
        Message = "Failed to dispatch {EventName} handler.")]
    public static partial void LogFailedToDispatchHandler(
        this ILogger logger,
        Exception ex,
        string eventName);

    [LoggerMessage(
        EventId = 4,
        Level = LogLevel.Information,
        Message = "Component mounting was cancelled")]
    public static partial void LogComponentMountingCancelled(
        this ILogger logger);

    [LoggerMessage(
        EventId = 5,
        Level = LogLevel.Error,
        Message = "Error mounting component {ComponentType}")]
    public static partial void LogErrorMountingComponent(
        this ILogger logger,
        Exception ex,
        string componentType);

    [LoggerMessage(
        EventId = 6,
        Level = LogLevel.Error,
        Message = "Error applying component edits for component {ComponentId}")]
    public static partial void LogErrorApplyingComponentEdits(
        this ILogger logger,
        Exception ex,
        int componentId);

    [LoggerMessage(
        EventId = 7,
        Level = LogLevel.Error,
        Message = "Error in UpdateDisplayAsync")]
    public static partial void LogErrorInUpdateDisplayAsync(
        this ILogger logger,
        Exception ex);

    [LoggerMessage(
        EventId = 8,
        Level = LogLevel.Error,
        Message = "Error occurred during rendering")]
    public static partial void LogErrorDuringRendering(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = 9,
        Level = LogLevel.Warning,
        Message = "Failed to translate VNode to renderable")]
    public static partial void LogFailedToTranslateVNode(
        this ILogger logger);

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Error,
        Message = "Error creating render snapshot")]
    public static partial void LogErrorCreatingRenderSnapshot(
        this ILogger logger,
        Exception ex);

    [LoggerMessage(
        EventId = 11,
        Level = LogLevel.Error,
        Message = "Element VNode does not have a tag name")]
    public static partial void LogElementVNodeMissingTagName(
        this ILogger logger);

    [LoggerMessage(
        EventId = 12,
        Level = LogLevel.Error,
        Message = "Error notifying observer of render snapshot")]
    public static partial void LogErrorNotifyingObserverOfSnapshot(
        this ILogger logger,
        Exception ex);

    [LoggerMessage(
        EventId = 13,
        Level = LogLevel.Error,
        Message = "Error notifying observer of error")]
    public static partial void LogErrorNotifyingObserverOfError(
        this ILogger logger,
        Exception ex);

    [LoggerMessage(
        EventId = 14,
        Level = LogLevel.Error,
        Message = "Attempted to dispatch event with invalid handler ID")]
    public static partial void LogInvalidHandlerId(
        this ILogger logger);

    [LoggerMessage(
        EventId = 15,
        Level = LogLevel.Error,
        Message = "Attempted to dispatch event with null eventArgs")]
    public static partial void LogNullEventArgs(
        this ILogger logger);

    [LoggerMessage(
        EventId = 16,
        Level = LogLevel.Error,
        Message = "Error dispatching event with handler ID {HandlerId}")]
    public static partial void LogErrorDispatchingEvent(
        this ILogger logger,
        Exception ex,
        ulong handlerId);

    [LoggerMessage(
        EventId = 17,
        Level = LogLevel.Error,
        Message = "Error completing observer")]
    public static partial void LogErrorCompletingObserver(
        this ILogger logger,
        Exception ex);

    [LoggerMessage(
        EventId = 18,
        Level = LogLevel.Error,
        Message = "Error completing observers during dispose")]
    public static partial void LogErrorCompletingObserversDuringDispose(
        this ILogger logger,
        Exception ex);

    [LoggerMessage(
        EventId = 19,
        Level = LogLevel.Warning,
        Message = "The LegacySpectre rendering pipeline is deprecated and will be completely removed in v1.1.0. Use WidgetLayout instead by removing the RAZORCONSOLE_RENDERING_PIPELINE override or setting ConsoleAppOptions.RenderingPipeline to WidgetLayout. The legacy pipeline does not support layout metadata or coordinate-aware mouse events.")]
    public static partial void LogLegacySpectrePipelineDeprecated(
        this ILogger logger);
}
