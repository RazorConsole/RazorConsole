# GitHub Copilot instructions

These instructions are automatically appended to Copilot Chat sessions when working in this repository. They provide extra context so that answers respect the codebase conventions and release process.

> ℹ️ Learn more about custom instructions in the official documentation: [Customize chat responses and set context](https://learn.microsoft.com/en-us/visualstudio/ide/copilot-chat-context?view=vs-2022#enable-custom-instructions).

## Project context

- RazorConsole renders Razor components to Spectre.Console output. The core implementation lives in `src/RazorConsole.Core` and the interactive showcase in `gallery/RazorConsole.Gallery`.
- Tests live in `src/RazorConsole.Tests` and should be kept up to date for any behavioral change.
- Design notes are available under `design-doc/` for additional background.

## Coding conventions

- Follow the rules encoded in `.editorconfig` (four-space indentation, file-scoped namespaces, system usings first).
- Prefer async/await with `ConfigureAwait(false)` when awaiting inside library code.
- Keep public APIs nullable-enabled and document exceptions and edge cases.
- Treat Spectre.Console renderables as immutable from outside rendering loops.

## Development workflow

- Run `dotnet format RazorConsole.slnx` before opening a pull request to ensure formatting checks pass.
- Execute `dotnet test RazorConsole.slnx` locally; CI requires a clean test run on Linux and Windows.
- When touching focus or keyboard handling, add or update tests in `FocusManagerTests` or `KeyboardEventManagerTests`.
- Update the README when introducing user-facing features or significant architectural changes.

## Release Note guidance
- Check out the version to be released from `VersionPrefix` in `nuget-package.props`, saying it's `0.1.1` for example.
- Query all completed GitHub Issues/PRs under `https://github.com/RazorConsole/RazorConsole`'s `v{VersionPrefix}` milestone. They would be added to that milestone when they're completed or their associated PR is merged, so you can use this approach to collect all changes included in the current release.
- Draft the release notes based on the collected issues and PRs and save it under `release-notes/` folder with the filename format `v{VersionPrefix}.md`.
- The release notes should include:
  - A summary of highlights, including new components, major bug fixes, notable improvements, release date, etc.
  - A detailed list of changes grouped by categories like "Components & Rendering", "Focus & Input Reliability", "Samples & Gallery", "Tooling, Docs & Website", and "Build & CI".
  - References to issue numbers, link to issue and commit hashes where applicable.
  - Acknowledgment of contributors for significant changes.
- Review README.md/design-docs/website/src/docs for any updates needed to reflect the new release.

## Prompting tips

- Ask for focused edits (specific files or components) rather than broad refactors.
- Provide failing test names or stack traces when requesting debugging assistance.
- Mention whether code runs in the live Spectre display or fallback rendering path to receive context-aware suggestions.
