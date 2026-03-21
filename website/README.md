# RazorConsole Documentation Website

This is the official documentation and showcase website for **RazorConsole**, a framework for building rich Terminal User Interfaces (TUI) using C# and Razor syntax.

The site is built as a highly optimized **Static Site (SSG)** to ensure maximum performance, perfect SEO, and easy hosting on GitHub Pages.

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
│   └── build-wasm.js       # Compiles RazorConsole.Website (.NET) to WASM for browser previews
├── src/
│   ├── assets/             # Static assets (images, global icons)
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
├── public/                 # Static files (favicons, robots.txt, 404.html)
├── react-router.config.ts  # SSG Configuration (Routes to pre-render)
└── vite.config.ts          # Build tool config (Path aliases, plugins)
```

## 📖 Architecture Notes

### API Documentation Flow

1.  **Extraction**: `docfx` scans the `RazorConsole.Core` project and outputs YAML files to `src/.docfx/`.
2.  **Indexing**: `api-docs.ts` uses Vite's `import.meta.glob` to eager-load these YAML files.
3.  **Sanitization**: During parsing, member UIDs (like `Scrollable`1` ) are converted to URL-friendly slugs (like  `Scrollable-1\`).
4.  **Rendering**: `ApiDocs.tsx` uses route loaders to find and display the correct metadata based on the URL.

### Theming Strategy

To avoid the "White Flash" (FOUC), we use a small blocking script in the `<head>` of `root.tsx`. It reads the theme preference directly from `localStorage` and applies the `.dark` class to the `<html>` element before React even starts rendering.

Code previews are rendered at build-time using `Shiki`. It sets two theme color variables in `style` attribute of the each text element. In `index.css` `Shiki` is styled to match the theme.

## 📤 Deployment

The website is automatically deployed to GitHub Pages via **GitHub Actions**.

  - The build process injects the repository name as a `basename` (e.g., `/RazorConsole/`).
  - A `404.html` fallback is generated to support SPA routing on static hosts.

## License

MIT License - see the [LICENSE](https://github.com/RazorConsole/RazorConsole/blob/main/LICENSE) file in the root of the repository.
