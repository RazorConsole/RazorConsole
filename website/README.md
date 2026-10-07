# RazorConsole Documentation Website

This is the official documentation and showcase website for **RazorConsole**, a framework for building rich Terminal User Interfaces (TUI) using C# and Razor syntax.

The site is built as a **Static Site (SSG)** with readable initial HTML and page-specific metadata for hosting on GitHub Pages.

## 🚀 Key Features

  - **Full SSG Support**: Every page, including hundreds of API reference nodes, is pre-rendered into static HTML during build time, using **React Router v7**.
  - **Interactive WASM Previews**: Real-time console rendering using .NET WASM and XTerm.js directly in the browser.
  - **Automated API Reference**: Documentation is automatically synchronized with C\# source code via DocFX.
  - **SEO & Accessibility**: Optimized meta tags for every sub-page and 90+ Lighthouse scores for accessibility.

## 🛠 Technology Stack

  - **Framework**: [React Router v7](https://reactrouter.com/) (configured in `ssr: false` mode for static generation).
  - **Build Tool**: [Vite](https://vite.dev/) with advanced code-splitting.
  - **Styling**: [Tailwind CSS](https://tailwindcss.com/) with custom typography.
  - **Syntax Highlighting**: [Shiki](https://shiki.style/) using dual-theme CSS variables.
  - **Metadata**: [DocFX](https://dotnet.github.io/docfx/) for extracting C\# documentation into YAML.
  - **TUI Web Rendering**: [.NET 10 WASM](https://learn.microsoft.com/uk-ua/aspnet/core/blazor/webassembly-build-tools-and-aot?view=aspnetcore-10.0) + [XTerm.js](https://xtermjs.org/).

## 💻 Development

### Prerequisites

  - **Node.js**: v20.x or later.
  - **.NET SDK**: v10.0 (as specified in `global.json`).
  - **DocFX**: Restored via `dotnet tool restore`.

### Installation

```bash
cd website
npm install
dotnet tool restore
```

### Mandatory Setup (Generating Data)

Before running the development server, you **must** generate the API metadata and build the WASM binaries, otherwise the API and Showcase sections will be empty:

```bash
# Generate API YAML files from C# source
npm run build:docfx

# Build the .NET Website project to WASM
npm run build:wasm
```

> [!WARNING]
> Metadata (like llms.txt, sitemap.xml, open graph images) is generated automatically after the main build, and stored on the production folder
> so when running `npm run dev`, you don't see any of them. 
> If you want to see website with metadata, run `npm run build` and then `npm run preview`.

### Running the Project

```bash
# Start development server
npm run dev

# Build for production (SSG)
npm run build

# Preview the static production build locally
npm run preview
```

## 🏗 Project Structure

```
website/
├── scripts/                # Build orchestration scripts
│   ├── build-wasm.js       # Compiles RazorConsole.Website (.NET) to WASM for browser previews
│   ├── generate-llms.ts    # Generates AI-friendly documentation (llms.txt, llms-full.txt)
│   ├── generate-og.tsx     # Generates dynamic OG social images using Satori and WASM runtime
│   └── generate-sitemap.ts # Generates sitemap.xml from indexable prerendered HTML
├── src/
│   ├── assets/             # Static assets (images, global icons, fonts)
│   ├── components/         # Reusable React components
│   │   ├── api/            # API Reference specific (TocTree, Sidebar, ApiDocument)
│   │   ├── app/            # Global Shell (Header, Footer, Layout, Theme handling)
│   │   ├── ...             # Collaborators, Showcase, Home, Components pages
│   │   └── ui/             # Core UI primitives (Buttons, Cards, Shiki CodeBlock, Markdown)
│   ├── data/               # Static data and Data-Fetching logic
│   │   ├── api-docs.ts     # Critical: Parses DocFX YAMLs and sanitizes UIDs for URLs
│   │   ├── components.ts   # Metadata for the interactive component gallery
│   │   ├── docs-ids.ts     # Navigation IDs for manual markdown docs
│   │   └── showcase.ts     # List of community projects
│   ├── docs/               # Manual documentation source (.md files)
│   ├── hooks/              # Custom React hooks (useDotNet, useTheme, useGitHubStars)
│   ├── lib/                # Utility libraries (XTerm initialization, path utils)
│   ├── pages/              # Route components and Loaders
│   │   ├── components/     # Nested routes for /components (Overview, Detail)
│   │   ├── Advanced.tsx    # Page for complex topics
│   │   ├── ApiDocs.tsx     # Dynamic page rendering DocFX metadata
│   │   ├── Docs.tsx        # Dynamic page rendering manual Markdown
│   │   └── ...             # Home, Showcase, Collaborators
│   ├── types/              # TypeScript interfaces and type definitions
│   ├── entry.client.tsx    # React Router client entry point
│   ├── root.tsx            # Root Layout: Meta tags, Global Scripts, Theme initialization
│   ├── routes.ts           # Unified route definitions for React Router v7
│   └── index.css           # Global styles and Tailwind directives
├── public/                 # Static files 
├── react-router.config.ts  # SSG Configuration (Routes to pre-render)
└── vite.config.ts          # Build tool config (Path aliases, plugins)
```

## 📖 Architecture Notes

### API Documentation Flow

1.  **Extraction**: `docfx` scans the `RazorConsole.Core` project and outputs YAML files to `src/.docfx/`.
2.  **Indexing**: `api-docs.ts` uses Vite's `import.meta.glob` to eager-load these YAML files.
3.  **Sanitization**: During parsing, member UIDs (like `Scrollable`1` ) are converted to URL-friendly slugs (like `Scrollable-1\`).
4.  **Rendering**: `ApiDocs.tsx` uses route loaders to find and display the correct metadata based on the URL.

### Metadata & SEO Generation (`build:metadata`)

This stage is executed by `build:metadata` after prerendering in `npm run build`:

1.  **AI Discovery (`llms.txt`)**: The script collects all guides and API components into a single, comprehensive plain text format. This allows AI tools (Cursor, GPT-4) to immediately obtain the context of the entire library.
2.  **SEO Automation (`sitemap.xml`)**: Reads the generated `build/client/**/index.html` files, includes their unique self-canonicals, and excludes meta-refresh/noindex redirects. This includes component, gallery, showcase, collaborators, tutorial, blog, release, and API pages. `lastmod` is omitted because build time is not a trustworthy content modification date. Missing or duplicate canonicals fail generation.
3.  **Dynamic Open Graph Images (`generate-og.ts`)**: Creates unique social media preview images for each component page.
    * **TUI Snapshot**: The script initializes a headless terminal [`@xterm/headless`](https://github.com/xtermjs/xterm.js) and loads the .NET WASM runtime.
    * **Image Rendering**: Utilizes the [`@chenglou/pretext`](https://github.com/chenglou/pretext) library for precise monospace font measurement and [`satori`](https://github.com/vercel/satori) to convert HTML/CSS into SVG.
    * **Consistency**: Each image reflects the actual state of the component (borders, scrollbars) directly from the library's source code.
    * **Selective Generation**: Supports an optional `--componentName` CLI flag to generate or update a preview for a single specific component, significantly reducing iteration time during development. (e.g., `npm run gen:og -- --componentName="Scrollable"`)

### Vite SSR Integration

The OG and LLMS generators use `vite.ssrLoadModule` to load current project data. The sitemap generator
instead reads the prerendered HTML so its URLs reflect the pages actually produced by the build.

---
### Theming Strategy

To avoid the "White Flash" (FOUC), we use a small blocking script in the `<head>` of `root.tsx`. It reads the theme preference directly from `localStorage` and applies the `.dark` class to the `<html>` element before React even starts rendering.

Code previews are rendered at build-time using `Shiki`. It sets two theme color variables in `style` attribute of the each text element. In `index.css` `Shiki` is styled to match the theme.

## 📤 Deployment

The authoritative website source remains in this repository. Production at
`https://razorconsole.github.io/` is built by `RazorConsole/RazorConsole.github.io`,
which checks out this repository into `source/` and uses:

```text
VITE_SITE_URL=https://razorconsole.github.io
VITE_BASE=/
VITE_ROUTER_BASENAME=/
```

Pull requests continue to publish root-based Cloudflare previews. This repository's existing Pages
deployment keeps serving the complete `/RazorConsole/` project site until the root site is proven
live. It switches to the generated redirect-only artifact only when the repository variable
`WEBSITE_DEPLOYMENT_MODE` is explicitly set to `redirects`; any other value preserves the current
deployment.

### SEO regression checks

After generating DocFX and WASM data, run a root-production build in PowerShell:

```powershell
$env:VITE_SITE_URL = "https://razorconsole.github.io"
$env:VITE_BASE = "/"
$env:VITE_ROUTER_BASENAME = "/"
npm run test:seo
npx tsc -b
npx react-router build
npm run gen:sitemap
npm run test:seo:static
python tests/tutorial_navigation.py
npm run gen:legacy-redirects
npm run test:legacy-redirects
```

`npm run build` also generates the social images and AI documentation. The focused sequence above
checks SEO without regenerating those unrelated assets. The static tests inspect HTML before JavaScript
runs: H1s, unique self-canonicals, Open Graph URLs, readable API descriptions, internal links, redirects,
and sitemap coverage. CI runs these checks after its full website build for preview and root-production
paths, plus a `/RazorConsole/` compatibility build. Keep `VITE_BASE` and `VITE_ROUTER_BASENAME`
aligned. Preview deployments can set their own `VITE_SITE_URL`; local navigation and assets remain
local rather than linking to production.

`SiteLink` and `site-paths.ts` normalize HTML routes to trailing slashes while preserving query strings,
anchors, files, and the project base path. Existing redirect routes remain available. Markdown uses
the shared `document-links.ts` normalization for legacy absolute production links, so they resolve
locally in previews; component route casing is normalized without changing file names.
GitHub Pages can still serve `/index.html` aliases; their generated HTML points to the directory canonical rather than
depending on host-level redirect rules. The client replaces only `index.html` aliases before
hydration, retaining the query, fragment, and existing history state so the router matches the page.

### Root migration and owner runbook

GitHub Pages cannot configure real HTTP 301 responses for project sites. `npm run
gen:legacy-redirects` therefore derives one minimal HTML redirect for every canonical route in the
root build. Each page is `noindex, follow`, declares the root canonical, and targets the final root
URL directly. JavaScript preserves query strings and fragments; the meta-refresh fallback cannot
reliably preserve them when JavaScript is disabled. The artifact also retains the Google verification
file, publishes a sitemap containing the new root URLs, and provides a noindex 404 fallback. It does
not copy the old site's content or assets, and it intentionally does not add an ineffective
`/RazorConsole/robots.txt`.

Use this order; do not combine the first two steps:

1. Merge and configure `RazorConsole/RazorConsole.github.io`, then verify the root Pages deployment,
   representative root canonicals, assets, Google verification file, sitemap, and browser navigation.
2. Set this repository's `WEBSITE_DEPLOYMENT_MODE=redirects`, then manually run **Activate legacy
   website redirects** with its root-live confirmation. This isolated workflow validates the live root
   site and deploys only redirects; it cannot publish packages or nightly releases. Verify representative
   and generated old routes redirect directly to their root equivalents. Keep these redirects deployed
   long term. Future `main` website deployments retain redirect mode. To roll back before search
   migration, unset the variable and rerun `main` CI.
3. Add the Search Console URL-prefix property `https://razorconsole.github.io/`, submit
   `https://razorconsole.github.io/sitemap.xml`, and inspect representative home, component, tutorial,
   blog, API, and release URLs. Retain the old `https://razorconsole.github.io/RazorConsole/` property
   to monitor old URL coverage and redirects. Do **not** use Change of Address: this is a same-host
   subdirectory-to-root move, which that tool does not support.

The existing verification token/file does not prove Search Console access, sitemap submission,
indexing, or Google's selected canonical. Record those separately and do not close #354 or #355
without the corresponding evidence.

For a read-only post-deployment check, run `npm run check:seo:deployed` (or append
`-- https://your-preview.example` to inspect a preview). This checks representative HTTP responses,
initial HTML headings/canonicals, sitemap coverage, and the origin-root robots status. It does not
authenticate to Search Console, interpret every robots directive, or claim Google has indexed a page.
Before deployment it may correctly fail against the old live site.

The root repository owns any optional origin-wide robots policy. Review existing rules before adding
one; missing robots.txt does not itself block crawling. If adopted, the root-only content is:

```text
User-agent: *
Allow: /

Sitemap: https://razorconsole.github.io/sitemap.xml
```

Source changes do not need cross-repository credentials to reach production: the root repository has
`workflow_dispatch` and a schedule fallback and checks out `main`. A real-time source-`main` trigger
would require an explicitly managed fine-grained PAT, GitHub App, or organization automation. Do not
hardcode a credential or make source CI fail when one is absent; this repository does not deploy Pages
to another repository.

### Search-led content and evidence

The blog includes a .NET TUI comparison article covering programming-model fit, built-in input, and
native distribution rather than unsupported “best framework” or speed claims. Its citations identify
the documentation/version scope and should be rechecked when updating dependencies.

No Search Console, Keyword Planner, or Google account integration is configured in this repository
or the available session tools. Public research does not supply private query or conversion data.
See `scripts/README.md` for the recorded research status and the distinction between product-fit
priorities and measured search demand.

The homepage keeps its stable H1 above the interactive Code/Preview, followed by full-width benefit
cards. Its final content section uses native `details`/`summary` for keyboard-accessible FAQs;
all answers and documentation links are present in the initial HTML. Static regression checks cover
this reading order, the exact H1, complete answers, and the existing site-wide link/heading rules.

## License

MIT License - see the [LICENSE](https://github.com/RazorConsole/RazorConsole/blob/main/LICENSE) file in the root of the repository.
