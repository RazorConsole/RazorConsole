import assert from "node:assert/strict"
import { existsSync, readFileSync } from "node:fs"
import { resolve, join } from "node:path"
import { test } from "node:test"
import { JSDOM } from "jsdom"
import { readStaticPages } from "./static-pages"
import { sitemapXml } from "./sitemap"
import { pagePath } from "../src/lib/site-paths"

const output = resolve("build/client")
const pages = readStaticPages(output)
const indexable = pages.filter((page) => page.indexable)
const home = indexable.find((page) => page.title.startsWith("RazorConsole:"))!
assert.ok(home, "Prerendered homepage must exist")
const site = new URL(home.canonical!)
const siteBase = site.pathname.replace(/\/$/, "")
const urlFor = (route: string) => `${site.origin}${siteBase}${pagePath(route)}`
const find = (route: string) => indexable.find((page) => page.canonical === urlFor(route))
const routeFiles = new Set(pages.map((page) => page.route))
const deployedRoot = home.route === "/" ? output : join(output, home.route)

test("all indexable HTML has one self-canonical, readable metadata and one meaningful H1", () => {
  assert.ok(indexable.length > 50, "Missing generated content: API metadata is required")
  if (process.env.VITE_SITE_URL) {
    const origin = new URL(process.env.VITE_SITE_URL).origin
    assert.equal(home.canonical, `${origin}${pagePath(process.env.VITE_BASE || "/")}`)
  }
  const seen = new Set<string>()
  for (const page of indexable) {
    assert.equal(page.canonicalCount, 1, page.route)
    const expectedPath = home.route === "/" ? `${siteBase}${page.route}` : page.route
    assert.equal(page.canonical, `${site.origin}${expectedPath}`, page.route)
    assert.ok(!seen.has(page.canonical!), `Duplicate canonical: ${page.route}`)
    seen.add(page.canonical!)
    assert.equal(page.ogUrl, page.canonical, page.route)
    assert.ok(page.title && page.description, `Missing metadata: ${page.route}`)
    assert.doesNotMatch(page.description, /<\/?(?:xref|p|see|code)\b|Full API reference for RazorConsole/)
    assert.equal(page.headings.length, 1, `${page.route}: ${page.headings.join(" | ")}`)
    assert.ok(page.headings[0], page.route)
  }
})

test("home positioning and representative routes exist before JavaScript", () => {
  assert.deepEqual(home.headings, ["Build C# terminal UIs with Razor components"])
  const html = readFileSync(home.file, "utf8")
  for (const text of ["Ink for .NET", "keyboard", "mouse", "experimental", "NativeAOT", "Preview", "google-site-verification"]) {
    assert.ok(html.includes(text), `Missing homepage content: ${text}`)
  }
  assert.ok(html.includes("jF1dcSGbDQJm6UY_MriNs2wHdnEGr_M1wZKiVciIdf8"), "Preserve the existing verification token")
  for (const route of ["/components", "/gallery", "/showcase", "/collaborators", "/components/table", "/docs/tutorial/hello-world", "/blog/hot-reload", "/api", "/api/RazorConsole.Components.SpectreTable", "/release-notes/v0.5.0", "/guides", "/guides/csharp-terminal-ui", "/guides/choosing-dotnet-tui"]) {
    assert.ok(find(route), `Missing canonical route: ${route}`)
  }
  assert.ok(home.links.includes(`${siteBase}/docs/tutorial/hello-world/`))
  const tableApi = find("/api/RazorConsole.Components.SpectreTable")!
  assert.ok(tableApi.links.includes(`${siteBase}/components/table/`))
  assert.equal(tableApi.ogTitle, tableApi.title)
  assert.equal(tableApi.ogDescription, tableApi.description)
})

test("every emitted internal page link resolves to generated output and uses a final slash", () => {
  const failures: string[] = []
  for (const page of indexable) {
    for (const href of page.links) {
      const link = new URL(href, page.canonical)
      if (link.origin !== site.origin || !link.pathname.startsWith(`${siteBase}/`)) continue
      const pathname = decodeURIComponent(link.pathname)
      // Asset/raw-document generation is independent of the HTML prerender step.
      if (!pathname.endsWith("/") && pagePath(pathname) === pathname) continue
      const route = siteBase ? pathname.slice(siteBase.length) : pathname
      const builtRoute = home.route === "/" ? route : pathname
      if (routeFiles.has(builtRoute)) continue
      if (existsSync(join(deployedRoot, route))) {
        if (!pathname.endsWith("/")) {
          failures.push(`${page.route} -> ${href} (missing trailing slash)`)
        }
        continue
      }
      failures.push(`${page.route} -> ${href} (missing output)`)
    }
  }
  assert.deepEqual([...new Set(failures)], [])
})

test("sitemap includes exactly the indexable canonical pages, with no fabricated lastmod", () => {
  const xml = readFileSync(join(output, "sitemap.xml"), "utf8")
  assert.equal(xml, sitemapXml(pages))
  const dom = new JSDOM(xml, { contentType: "text/xml" })
  const urls = [...dom.window.document.querySelectorAll("loc")].map((node) => node.textContent)
  assert.equal(urls.length, indexable.length)
  assert.equal(new Set(urls).size, urls.length)
  assert.doesNotMatch(xml, /<lastmod>|index\.html|<priority>|<changefreq>/)
  dom.window.close()
})

test("legacy blog and quick-start redirects remain noindex and target existing pages", () => {
  for (const route of ["/blog/", "/docs/quick-start/"]) {
    const page = pages.find((candidate) => candidate.route === `${home.route === "/" ? "" : siteBase}${route}`)
    assert.ok(page?.refresh, `Missing redirect ${route}`)
    assert.equal(page.indexable, false)
    const target = page.refresh.match(/url=(.+)$/i)?.[1]?.replace(/^["']|["']$/g, "")
    assert.ok(target, `Missing redirect target ${route}`)
    const destination = new URL(target, urlFor(route))
    assert.ok(indexable.some((candidate) => candidate.canonical === destination.href), `${route} -> ${destination.href}`)
  }
})
