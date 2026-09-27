# RazorConsole tutorial

This folder keeps each lesson beside the source code that powers its examples.

- `chapters/` contains the lesson text consumed by the website.
- `Tutorial.Components/` contains the Razor components shared by the browser preview and native runner.
- `Tutorial.Runner/` runs the same components in a local terminal.

## Run a chapter from this checkout

The repository currently pins the preview .NET SDK in [`global.json`](../global.json). From the
repository root, run:

```shell
dotnet run --project tutorial/Tutorial.Runner
```

Press <kbd>Enter</kbd> to activate the focused button and <kbd>Ctrl+C</kbd> to exit.

Each later chapter is selected with one runner flag:

```shell
dotnet run --project tutorial/Tutorial.Runner -- --state-events
dotnet run --project tutorial/Tutorial.Runner -- --text-input
dotnet run --project tutorial/Tutorial.Runner -- --mouse-events
dotnet run --project tutorial/Tutorial.Runner -- --layout
dotnet run --project tutorial/Tutorial.Runner -- --routing
dotnet run --project tutorial/Tutorial.Runner -- --async-work
dotnet run --project tutorial/Tutorial.Runner -- --complete-app
```

The runner enables native terminal mouse reporting for Chapter 2. Hover and click the first card, scroll over
the lower canvas, or drag the blue card. Arrow keys move the card after it receives focus.
