---
name: razorconsole
description: "Use this skill whenever writing, changing or reviewing a RazorConsole app: .razor components for the terminal, UseRazorConsole setup, RazorConsole components such as Panel, Select, TextInput or Table, focus and key handling, hot reload, or any 'add X to the counter example' request in a project that references RazorConsole.Core. Trigger even when the user does not name RazorConsole but the project builds Razor components that render in a console."
---

# RazorConsole

Use this workflow to build RazorConsole apps from the project's documentation instead of from memory. Component names, parameters and APIs change between versions, so a guess that compiles today can be wrong tomorrow.

## Goal

Every component, parameter and API in the code you write comes from the RazorConsole documentation or from code already in the project. Nothing comes from memory or from other UI frameworks.

## Workflow

1. **Read the docs index first**
   - Fetch https://razorconsole.com/llms.txt before writing any code.
   - Open the page it links for the component or feature you are about to use (a component, focus, hosting, hot reload, testing, the command-line tool).
   - If the page cannot be fetched, say so and ask the user for the docs, or read the source of the referenced RazorConsole packages. Do not fill the gap from memory.

2. **Look at the project**
   - Read the app's entry point, the component you will change and its sibling components. Match their structure, naming and options.
   - Check which RazorConsole package versions the project references; the docs you read apply to the released version.

3. **Write the change**
   - Use only names and parameters you found in step 1 or in the project.
   - When you need to drive an element later (a click, a wait, an assertion), give it a `data-vnode-hook` attribute. It is the most stable selector for the inspect tools.

4. **Build**
   - Run `dotnet build` for the project and fix compile errors before anything else. A compile error on a component or parameter means you guessed: go back to the docs page.

5. **Verify in the running app**
   - Use the `razorconsole-inspect` skill to start the app, look at the screen and act on it. Do not report a UI change as working because it compiled.

## Definition of done

- The code builds.
- Each RazorConsole component and parameter used is documented on a page you read, or already used in the project.
- The changed screen was run and checked, or you told the user that you could not run it and why.

## Anti-patterns

- Writing components, parameters or options from memory.
- Copying a pattern from another UI framework (HTML elements, CSS, Blazor-only features) without checking that RazorConsole supports it.
- Treating a successful build as proof that the screen looks right.
