import { mkdirSync, writeFileSync } from "node:fs"
import { dirname, join } from "node:path"
import type { StaticPage } from "./static-pages"
import { sitemapXml } from "./sitemap"

export const legacyProjectPath = "/RazorConsole"

export function redirectLocation(target: string, search: string, hash: string): string {
  return `${target}${search}${hash}`
}

const escapeHtml = (value: string) => value
  .replace(/&/g, "&amp;")
  .replace(/</g, "&lt;")
  .replace(/"/g, "&quot;")

const scriptString = (value: string) => JSON.stringify(value).replace(/</g, "\\u003c")

export function legacyRedirectHtml(target: string): string {
  const escapedTarget = escapeHtml(target)
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="robots" content="noindex, follow">
  <meta name="referrer" content="no-referrer">
  <link rel="canonical" href="${escapedTarget}">
  <meta http-equiv="refresh" content="0; url=${escapedTarget}">
  <title>RazorConsole documentation moved</title>
  <script>
    location.replace(${scriptString(target)} + location.search + location.hash)
  </script>
</head>
<body>
  <p>This page moved to <a href="${escapedTarget}">${escapedTarget}</a>.</p>
</body>
</html>
`
}

function routeFromCanonical(canonical: string, rootSite: URL): string {
  const url = new URL(canonical)
  if (url.origin !== rootSite.origin || !url.pathname.startsWith(rootSite.pathname)) {
    throw new Error(`Canonical is outside the root production site: ${canonical}`)
  }
  return url.pathname.slice(rootSite.pathname.replace(/\/$/, "").length).replace(/^\/+|\/+$/g, "")
}

export function generateLegacyRedirects(pages: StaticPage[], output: string, rootSite: string): number {
  const site = new URL(rootSite)
  if (site.pathname !== "/") throw new Error(`Root production site must not have a path: ${rootSite}`)

  const indexable = pages.filter((page) => page.indexable)
  const targets = new Set<string>()
  for (const page of indexable) {
    if (page.canonicalCount !== 1 || !page.canonical) {
      throw new Error(`Missing or duplicate canonical: ${page.file}`)
    }
    if (targets.has(page.canonical)) throw new Error(`Duplicate redirect target: ${page.canonical}`)
    targets.add(page.canonical)

    const route = routeFromCanonical(page.canonical, site)
    const file = join(output, route, "index.html")
    mkdirSync(dirname(file), { recursive: true })
    writeFileSync(file, legacyRedirectHtml(page.canonical))
  }

  writeFileSync(join(output, "sitemap.xml"), sitemapXml(indexable))
  return targets.size
}

export function legacyNotFoundHtml(rootSite: string): string {
  const site = new URL(rootSite)
  const root = site.origin
  return `<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="robots" content="noindex, follow">
  <link rel="canonical" href="${escapeHtml(root)}/">
  <title>RazorConsole documentation moved</title>
  <script>
    const path = location.pathname.replace(/^\\/RazorConsole(?=\\/|$)/, "") || "/"
    location.replace(${scriptString(root)} + path + location.search + location.hash)
  </script>
</head>
<body>
  <p>The RazorConsole documentation moved to <a href="${escapeHtml(root)}/">${escapeHtml(root)}/</a>.</p>
</body>
</html>
`
}
