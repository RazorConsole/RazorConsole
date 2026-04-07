# Build Automation Scripts

This directory contains the orchestration scripts that power the `prebuild` and `postbuild` stages. They ensure the project is SEO-optimized, AI-discoverable, visually compelling on social media, and correctly compiled for the web.

## 🚀 Overview

| Script                | Purpose                 | Key Output                                    |
| :-------------------- | :---------------------- | :-------------------------------------------- |
| **`build-wasm.js`**   | **Core Compilation**    | **.NET WASM Artifacts**                       |
| `generate-llms.ts`    | AI Context Discovery    | `llms.txt`, `llms-full.txt`, `build/raw/*.md` |
| `generate-sitemap.ts` | SEO Optimization        | `sitemap.xml`                                 |
| `generate-og.ts`      | Dynamic Social Previews | `build/og/*.png`                              |

---

## 🛠 Script Details & Technical API

### 1. WebAssembly Compilation (`build-wasm.js`)

The bridge between the .NET ecosystem and the web frontend. It ensures that the core TUI engine is available for both the live site and automation tools.

- **Technical Logic**: Uses Node.js `child_process` to trigger the `dotnet publish` pipeline. It specifically targets the `RazorConsole.Website` project to produce a `wwwroot/_framework` folder containing the mono-runtime and compressed `.wasm` binaries.
- **API / Usage**:

  ```bash
  npm run build:wasm
  ```

  - **Environment**: Requires .NET SDK installed on the host.
  - **Output**: `artifacts/publish/RazorConsole.Website/release/wwwroot/_framework/`

---

### 2. AI Context Discovery (`generate-llms.ts`)

Orchestrates the conversion of technical metadata into AI-readable knowledge bases.

- **Technical Logic**:
  - **SSR Data Access**: Uses `vite.ssrLoadModule` to bypass the build step and read the project's TypeScript data structures directly.
  - **Content Sanitization**: Implements `cleanXref` logic to strip DocFX-specific XML/YAML tags and convert them into clean Markdown.
  - **Aggregation**: Compiles two distinct formats: an index-based `llms.txt` for discovery and a flat `llms-full.txt` for deep context injection.
- **API / Usage**:

  ```bash
  npm run gen:llms
  ```

  - **Generated Files**:
    - `/build/llms.txt`: Sitemap for AIs.
    - `/build/llms-full.txt`: Full content bundle.
    - `/build/raw/`: Individual markdown files for every component and guide.

---

### 3. SEO Sitemap (`generate-sitemap.ts`)

A dynamic SEO generator that mirrors the React Router v7 route tree.

- **Technical Logic**:
  - **Route Mapping**: Combines manual documentation IDs, release notes, and auto-generated API UIDs (sanitizing special characters for URL safety).
  - **Prioritization Engine**: Implements a weighted algorithm that assigns `priority` based on the route depth and category (e.g., core components rank higher than individual API methods).
- **API / Usage**:
  ```bash
  npm run gen:sitemap
  ```

---

### 4. Dynamic OG Images (`generate-og.tsx`)

A sophisticated rendering pipeline that creates snapshots of TUI components without a browser.

- **Technical Logic**:
  - **Headless Runtime**: Boots a virtualized `.NET WASM` instance inside Node.js using `JSDOM` and `node-canvas`. It captures ANSI output from `@xterm/headless`.
  - **Sub-pixel Accuracy**: Employs `@chenglou/pretext` for sub-pixel font measurement.
  - **Multi-font Support**: Registers `Normal`, `Bold`, and `Italic` variations of Cascadia Code in both `node-canvas` (for measurement) and `Satori` (for rendering).
- **API / CLI Flags**:
  | Flag | Type |Description|
  | :--- | :--- | :--- |
  | `--componentName` | `string` | **Optional.** Renders only the specified component (e.g., `--componentName=Modal`). |
- **Usage**:

  ```bash
  # Generate everything (Postbuild default)
  npm run gen:og

  # Targeted update for development
  npm run gen:og -- --componentName=ComponentName
  ```

---

## 🏗 Technology Stack

| Layer             | Technology           | Role                                                    |
| :---------------- | :------------------- | :------------------------------------------------------ |
| **Execution**     | `tsx` / `Vite SSR`   | TypeScript execution with access to source modules.     |
| **DOM Emulation** | `jsdom`              | Faking a browser environment for WASM and Satori.       |
| **Font Engine**   | `canvas` + `pretext` | Measuring character widths for terminal grid alignment. |
| **SVG Layout**    | `satori`             | Converting React-like JSX/HTML into SVG.                |
| **Rasterization** | `@resvg/resvg-js`    | High-performance PNG generation from SVG.               |
| **Terminal**      | `@xterm/headless`    | Simulating the terminal buffer to capture C# output.    |
