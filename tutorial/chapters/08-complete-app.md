# Chapter 8 · Build a Complete Interactive App

Combine component state, text input, focus, mouse input, layout, and callbacks into a small task board.

<!-- interactive-preview -->

## Learning objectives

- design a feature as small state transitions;
- render a collection with stable keys;
- support keyboard and mouse interaction; and
- organize a production-ready next step.

## 1. Define the data model

The task board owns a list and a draft value:

```csharp
private string _draft = string.Empty;
private List<TaskItem> _tasks = [];

private sealed record TaskItem(Guid Id, string Title)
{
    public bool Done { get; set; }
}
```

Keep UI-only state in the component. Move persistence and external I/O into injected services so those concerns remain
testable without a terminal.

## 2. Add and render tasks

```razor
<TextInput Label="New task" @bind-Value="_draft" OnSubmit="AddAsync" />
<TextButton Content="Add task" OnClick="Add" />

@foreach (var task in _tasks)
{
    <div @key="task.Id" @onclick="() => Toggle(task)">
        <Panel>
            <Markup Content="@TaskLabel(task)" />
        </Panel>
    </div>
}
```

`@key` preserves the identity of each rendered row when items are added or removed. Clicking a task toggles it through
the raw mouse-event subscription learned in Chapter 4.

## 3. Make actions deterministic

Each callback performs one state transition:

```csharp
private void Add()
{
    var title = _draft.Trim();
    if (title.Length == 0) return;

    _tasks.Add(new(Guid.NewGuid(), title));
    _draft = string.Empty;
}

private void Toggle(TaskItem task) => task.Done = !task.Done;
private void ClearCompleted() => _tasks.RemoveAll(task => task.Done);
```

Small transitions are easy to test. The final example exposes add, toggle, clear, and reset without embedding terminal
input parsing in application code.

## 4. Prepare the app for real use

For a larger application, split task rows and editors into child components, inject a repository for persistence, add
routed detail pages, and wrap service calls in the loading/error pattern from Chapter 7. Retain keyboard alternatives
for every mouse-only action.

## Run the complete app locally

```shell
dotnet run --project tutorial/Tutorial.Runner -- --complete-app
```

Add tasks with the input, click existing tasks to toggle them, and use the focused action buttons with <kbd>Enter</kbd>.

## Final challenge

Persist tasks to a JSON file through an injected service and add a routed view that shows only completed tasks.

[← Chapter 7 · Async Work](/docs/tutorial/async-work)
