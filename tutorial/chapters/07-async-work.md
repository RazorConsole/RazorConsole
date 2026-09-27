# Chapter 7 · Async Work, Loading, and Errors

Keep the terminal responsive while work is in progress and render every operation state explicitly.

<!-- interactive-preview -->

## Learning objectives

- use asynchronous event callbacks;
- model loading, success, error, and cancellation;
- prevent duplicate work; and
- clean up cancellation resources.

## 1. Model operation state

An async UI should make its states explicit:

```razor
@if (_loading)
{
    <Spinner SpinnerType="Spinner.Known.Dots" />
}
else if (_error is not null)
{
    <Markup Content="@_error" Foreground="Color.Red" />
}
else
{
    <Markup Content="@_result" Foreground="Color.Green" />
}
```

Avoid blocking with `.Result`, `.Wait()`, or `Thread.Sleep`. Those prevent input and rendering from progressing.

## 2. Await work in the callback

```csharp
private async Task LoadAsync()
{
    _loading = true;
    _error = null;

    try
    {
        _result = await service.LoadAsync(_request.Token);
    }
    catch (OperationCanceledException)
    {
        _result = "Request cancelled.";
    }
    catch (Exception ex)
    {
        _error = ex.Message;
    }
    finally
    {
        _loading = false;
    }
}
```

Razor renders once when the callback yields and again when it completes. Guard actions while `_loading` or cancel the
previous request before starting another one, and expose cancellation when users may need to regain control.

## 3. Treat errors as UI state

Catch errors close enough to provide an actionable message, while still logging diagnostic details in the service or
host. A retry button can call the same async callback after the error is shown.

## Run this chapter locally

```shell
dotnet run --project tutorial/Tutorial.Runner -- --async-work
```

Try success, simulated failure, and cancellation. Each path returns to an interactive state.

## Exercise

Add a retry counter and use an increasing delay before each retry.

[← Chapter 6 · Routing](/docs/tutorial/routing) · [Next: Chapter 8 · Complete App →](/docs/tutorial/complete-app)
