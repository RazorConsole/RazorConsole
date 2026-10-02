# what's new in RazorConsole 0.6.0

RazorConsole 0.6.0 brings a new default layout engine, terminal mouse events, native app distribution, and an interactive learning path. This article introduces the changes relative to 0.5.0. For the complete change list and scope, read the [0.6.0 release notes](/release-notes/v0.6.0/).

RazorConsole lets you build terminal applications with Razor components: keep state and event handlers close to your UI, compose reusable components, and render into terminal cells rather than browser HTML. Version 0.6.0 concentrates on taking ownership of layout, connecting mouse input to components, making the official apps easier to try, and teaching the programming model through working examples.

## WidgetLayout becomes the default

The largest change is beneath the components. Starting with 0.6.0, an application that does not select a rendering pipeline uses **WidgetLayout** instead of the legacy Spectre pipeline.

Why does that matter? A terminal UI needs more than a picture of its content. It needs to know where a component is, how much space it has, which region scrolls, and where input should go. The widget-based engine calculates layout before rendering, making bounds and layout metadata available to the rest of the application. Spectre.Console remains part of the rendering foundation; this is not a claim that it has been removed.

For application authors, the important consequence is to **check layout and interaction together** when upgrading. Try a narrow terminal, resize while content is visible, tab through controls, and exercise scrolling and mouse input. A view that looks correct at one size is not the whole compatibility test.

The legacy renderer is still available for comparison. In PowerShell, set this before launching your application:

```text
$env:RAZORCONSOLE_RENDERING_PIPELINE = "LegacySpectre"
dotnet run
```

Remove the override when you want to return to WidgetLayout:

```text
Remove-Item Env:RAZORCONSOLE_RENDERING_PIPELINE
```

Existing `FlexBox`, `ITranslationMiddleware`, and `TranslationContext` APIs remain available. The important migration is behavioral: WidgetLayout does not invoke custom Spectre translators for every widget, and terminal/layout defaults change. The [Widget Layout guide](/blog/widget-layout/) and [custom translator guide](/blog/custom-translators/) explain how to adapt rendering extensions.

The engine work landed in [#339](https://github.com/RazorConsole/RazorConsole/pull/339), including regression coverage for rendering and scrolling. There are no new benchmark results here, so the change should not be read as a measured performance claim.

## Mouse events join keyboard input

RazorConsole 0.6.0 adds opt-in terminal mouse support, backed by native Windows and Unix input handling. WidgetLayout supplies the element bounds used to route terminal coordinates to the component under the pointer, so input and layout share the same view of the screen ([#339](https://github.com/RazorConsole/RazorConsole/pull/339)).

Components can use familiar Razor handlers: `@onclick`, `@onmousedown`, `@onmouseup`, `@onmousemove`, `@onmouseenter`, `@onmouseleave`, and `@onwheel`. That opens up clickable controls, hover feedback, dragging, and wheel scrolling without giving up keyboard interaction.

Enable `ConsoleAppOptions.ConsoleLiveDisplayOptions.EnableMouseEvents` in your host configuration. Mouse events are **off by default**, and enabling them also activates the alternate screen. Terminal mouse reporting must be supported by the terminal you run in.

Handlers receive `MouseEventArgs`, or `WheelEventArgs` for wheel input, from `Microsoft.AspNetCore.Components.Web`. The names are familiar, but the coordinates are terminal cells, not browser pixels: `ClientX` and `ClientY` are zero-based terminal positions, while `OffsetX` and `OffsetY` are relative to the handling node. Wheel events use line-based `DeltaY` values (`DeltaMode = 1`).

The event routing also supports left-click focus and drag capture. For example, `Select` options can be clicked, `Scrollable` responds to the wheel, and Snake demonstrates a draggable speed control. These interactions complement Tab and keyboard navigation rather than replace them. Try the [mouse-events tutorial](/docs/tutorial/mouse-events/) to explore the model; this is not a promise of complete browser pointer-event parity or identical behavior in every terminal.

## Try Gallery and Snake as native apps

The Gallery is the quickest way to explore the component collection. In this release, it gains NativeAOT distribution alongside its existing .NET tool package. **Snake** joins it as a complete, responsive terminal-game showcase.

The official native archive builds cover Linux, Windows, and macOS, each on x64 and ARM64. Shared installation scripts discover the appropriate app and platform archive, and offer two channels:

- **Stable:** the latest published stable release, not whatever is currently on `main`.
- **Nightly:** development builds for trying changes before a stable release.

To pin a particular version instead of following a moving channel, download the matching platform archive directly from that version's GitHub Release.

The installer work also addresses the Windows installation problem. See [#345](https://github.com/RazorConsole/RazorConsole/pull/345), [#346](https://github.com/RazorConsole/RazorConsole/pull/346), [#348](https://github.com/RazorConsole/RazorConsole/pull/348), and [#350](https://github.com/RazorConsole/RazorConsole/pull/350).

NativeAOT can make an app runnable without a separately installed .NET runtime. It does **not** remove platform, terminal, trimming, dependency, or asset considerations. Support remains experimental, and a successful native build is not proof that every interaction works on every terminal. Review the [NativeAOT guide](/blog/native-aot/) before applying the same approach to your own application.

The [Gallery documentation](/blog/component-gallery/) describes the available installation paths.

## Learn through eight interactive chapters

The getting-started path is now a preview-first tutorial rather than a single Quick Start page. Its eight chapters build from Hello World toward a complete application:

1. Hello World.
2. State and events.
3. Text input and focus.
4. Mouse events.
5. Widget layout and resize.
6. Routing.
7. Asynchronous work.
8. A complete app.

The browser preview and native `Tutorial.Runner` use the same Razor components. That makes the tutorial useful both for immediate experimentation and for understanding what runs in a real terminal. Start with [Hello World](/docs/tutorial/hello-world/), then change state, type into a control, and follow the effects through the UI.

The tutorial was rebuilt in [#343](https://github.com/RazorConsole/RazorConsole/pull/343). A later navigation fix in [#356](https://github.com/RazorConsole/RazorConsole/pull/356) separates server and client loader exports while sharing the actual chapter resolver. Built-output Chromium tests cover the root and project-subdirectory deployments, including client navigation, terminal input, and restart.

Those checks do not mean the browser terminal is free of every lifecycle issue: an intermittent xterm `Viewport._innerRefresh` diagnostic involving `dimensions` is still a known limitation.

## Smaller changes worth noticing

Version 0.6.0 also includes rendering-lock fixes ([#320](https://github.com/RazorConsole/RazorConsole/pull/320)) and cursor visibility control ([#325](https://github.com/RazorConsole/RazorConsole/pull/325)). The website gains static HTML and metadata improvements, clearer homepage navigation, and a comparison article in [Blog](/blog/choosing-dotnet-tui/).

The source checkout now includes a `net11.0` target alongside .NET 8, 9, and 10 and pins a .NET 11 preview SDK in `global.json`. That source-build requirement is different from the runtime needed by an application consuming a lower-target NuGet asset. Official native-app builds use `net10.0`.

One fix that is **not** included is the pending Windows 10 rendering change: [#316](https://github.com/RazorConsole/RazorConsole/issues/316) is still open, and [#341](https://github.com/RazorConsole/RazorConsole/pull/341) is not part of 0.6.0.

## Preparing an existing app for 0.6.0

When upgrading, update your application's `RazorConsole.Core` reference to `0.6.0`. Then review the [release notes' migration section](/release-notes/v0.6.0/#upgrade--migration), rebuild any rendering extensions, and compare the default and legacy pipelines where useful. Alternate-screen rendering and cursor hiding now default to on; mouse input remains a separate opt-in.

For NativeAOT applications, test the published binary on the platform and terminal you intend to support, not only a framework-dependent development build. For new applications, begin with the [interactive tutorial](/docs/tutorial/hello-world/) and use the Gallery to explore the controls.

The release is about making component-based terminal applications more coherent to build, try, and learn. Its limitations remain explicit: experimental NativeAOT support, terminal-specific behavior, and unresolved issues are part of the upgrade decision, not details hidden by a successful build.
