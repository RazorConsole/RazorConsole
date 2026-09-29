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

A sitemap generator that reads the actual React Router v7 prerendered HTML.

- **Technical Logic**:
  - **URL Discovery**: Reads `build/client/**/index.html`, including project-base output directories, and uses each indexable page's absolute canonical URL.
  - **Exclusions**: Skips meta-refresh/noindex pages and non-index HTML files such as the deployment's `404.html`. Missing or duplicate canonicals fail the command.
  - **Content Dates**: Omits `lastmod` rather than presenting build timestamps as content updates. It does not emit `priority` or `changefreq`.
- **API / Usage**:
  ```bash
  npm run gen:sitemap
  ```

  Run after `react-router build`. Use `npm run test:seo` for URL/text helpers and
  `npm run test:seo:static` after sitemap generation to inspect the produced HTML, metadata,
  internal links, headings, and sitemap. See the website README for production-base environment settings.

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

## Deployment verification boundary

`npm run check:seo:deployed` is a read-only HTTP/initial-HTML check, not a Google indexing tool.
On 2026-09-29 (UTC), the deployed home, table, first tutorial chapter, hot-reload blog post,
SpectreTable API page, and v0.5.0 release page all returned HTTP 200. All six lacked canonical links;
the home H1 was empty and the hot-reload post lacked an H1. These are **pre-deployment baseline**
observations, not results for the new PR output. Both origin-root and project-path robots.txt
returned 404. Missing robots does not mean the site is blocked.

The Pages API identified the current repository as a workflow-deployed project site at
`https://razorconsole.github.io/RazorConsole/`. A query for the conventional origin-root repository
returned 404, which does not distinguish nonexistence from unavailable access. No origin-root
deployment was changed. The website README contains the minimum owner follow-up and an optional
root-only robots example.

No authenticated Search Console or Keyword Planner integration is available to this session.
The existing public verification meta tag was not used as evidence of property authorization.
Submission, Google-selected canonical, impressions, queries, and indexing status remain unverified
until a property owner supplies or inspects those results.

## Search research record (29 September 2026 UTC)

The .NET TUI comparison article targets the non-brand task of choosing a terminal UI library. Its
priority is provisional and based on product fit and the existing interactive tutorial, **not
measured query volume, keyword difficulty, or an observed ranking**.

An actual public Google Trends request compared `C# terminal UI`, `.NET TUI`, and `C# console UI`
over the past five years, without a geography restriction:
[reproducible query](https://trends.google.com/trends/explore?date=today%205-y&q=C%23%20terminal%20UI,.NET%20TUI,C%23%20console%20UI&hl=en).
It returned **HTTP 429**. No chart, relative-interest values, search volumes, or “insufficient data”
result was obtained. No autocomplete response was substituted for demand evidence.

Google's [Trends FAQ](https://support.google.com/trends/answer/4365533?hl=en) was accessible.
It describes sampled, normalized 0–100 relative interest rather than absolute volumes and notes
that autocomplete does not necessarily reflect the most popular searches. Special characters
can affect interpretation; do not assume how the `C#` term was processed without actual results.

The comparison article uses pinned official sources for Spectre.Console 0.57.2,
Spectre.Console.Cli 0.55.0, and Terminal.Gui v2.5.0. It distinguishes source-declared AOT
compatibility from tested application behavior and does not claim competitor mouse support is absent.
Citations and review date are included in the published article.

Remaining data work requires an authorized property owner to export non-brand queries, landing
pages, impressions, and clicks from Search Console (with date range and filters recorded), or
authorized Keyword Planner/accessible Trends results. Use those observations to revisit content
priorities; do not backfill unavailable metrics. No follow-up issue is considered complete merely
because this collection procedure is documented.
