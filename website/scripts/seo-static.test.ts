import assert from "node:assert/strict"
import { existsSync, readFileSync } from "node:fs"
import { resolve, join } from "node:path"
import { test } from "node:test"
import { JSDOM } from "jsdom"
import { readStaticPages } from "./static-pages"
import { sitemapXml } from "./sitemap"
import { pagePath } from "../src/lib/site-paths"
import { releaseNoteIds } from "../src/data/docs-ids"

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
  assert.deepEqual(home.headings, ["Build TUI with Razor Component"])
  const html = readFileSync(home.file, "utf8")
  for (const text of ["keyboard", "mouse", "experimental", "NativeAOT", "Preview", "google-site-verification"]) {
    assert.ok(html.includes(text), `Missing homepage content: ${text}`)
  }
  assert.doesNotMatch(html, /Ink for \.NET/)
  assert.ok(html.includes("jF1dcSGbDQJm6UY_MriNs2wHdnEGr_M1wZKiVciIdf8"), "Preserve the existing verification token")
  for (const route of ["/components", "/gallery", "/showcase", "/collaborators", "/components/table", "/docs/tutorial/hello-world", "/blog/hot-reload", "/blog/choosing-dotnet-tui", "/api", "/api/RazorConsole.Components.SpectreTable", ...releaseNoteIds.map((note) => `/release-notes/${note.id}`)]) {
    assert.ok(find(route), `Missing canonical route: ${route}`)
  }
  assert.equal(find("/guides"), undefined, "TUI Guides route was removed")
  assert.ok(home.links.includes(`${siteBase}/docs/tutorial/hello-world/`))
  const tableApi = find("/api/RazorConsole.Components.SpectreTable")!
  assert.ok(tableApi.links.includes(`${siteBase}/components/table/`))
  assert.equal(tableApi.ogTitle, tableApi.title)
  assert.equal(tableApi.ogDescription, tableApi.description)
})

test("homepage initial DOM puts the demo before benefit cards and complete FAQ answers last", () => {
  const dom = new JSDOM(readFileSync(home.file, "utf8"))
  try {
    const document = dom.window.document
    const heading = document.querySelector("#home-hero-title")!
    const demo = document.querySelector("#home-demo")!
    const benefits = document.querySelector('section[aria-labelledby="why-razorconsole"]')!
    const faq = document.querySelector('section[aria-labelledby="home-faq-title"]')!
    const follows = dom.window.Node.DOCUMENT_POSITION_FOLLOWING
    assert.ok(heading && demo && benefits && faq)
    assert.ok(heading.compareDocumentPosition(demo) & follows)
    assert.ok(demo.compareDocumentPosition(benefits) & follows)
    assert.ok(benefits.compareDocumentPosition(faq) & follows)
    assert.equal(demo.parentElement, benefits.parentElement, "Demo and benefits share the full-width parent")
    assert.equal(benefits.querySelectorAll("h3").length, 3)
    assert.equal(faq.parentElement?.lastElementChild, faq, "FAQ is the final homepage content block")
    assert.equal(document.querySelectorAll("h1").length, 1)
    assert.equal(document.querySelectorAll("a a").length, 0)
    const questions = [
      ["What is RazorConsole?", "terminal cells, not browser HTML"],
      ["Do I need a web server or Blazor hosting?", "without a web server"],
      ["How is it related to Spectre.Console?", "rendering foundation"],
      ["Does it support mouse and keyboard input?", "enable mouse reporting"],
      ["Can I publish with NativeAOT?", "experimental support"],
      ["How do I get started?", "interactive tutorial"],
    ]
    const details = [...faq.querySelectorAll("details")]
    assert.equal(details.length, questions.length)
    details.forEach((item, index) => {
      assert.equal(item.firstElementChild?.tagName, "SUMMARY")
      assert.equal(item.querySelector("summary")?.textContent, questions[index][0])
      assert.ok(item.querySelector("p")?.textContent?.includes(questions[index][1]))
      assert.ok(item.querySelector("a[href]"), "Each answer links to existing documentation")
    })
  } finally {
    dom.window.close()
  }
})

test("0.6.0 notes show the published release while the introduction explains mouse input", () => {
  const release = find("/release-notes/v0.6.0")
  const blog = find("/blog/whats-new-in-razorconsole-0-6-0")
  assert.ok(release, "Missing 0.6.0 release notes")
  assert.ok(blog, "Missing 0.6.0 introduction")
  assert.equal(release.title, "v0.6.0 | RazorConsole Release Notes")
  assert.deepEqual(blog.headings, ["what's new in RazorConsole 0.6.0"])
  const releaseHtml = readFileSync(release.file, "utf8")
  assert.ok(releaseHtml.includes("Released: September 29, 2026"))
  assert.doesNotMatch(releaseHtml, /Unreleased|has not been published|release date is not yet set|Until the release exists/)
  assert.ok(release.links.includes("https://github.com/RazorConsole/RazorConsole/releases/tag/v0.6.0"))
  assert.ok(release.links.includes("https://github.com/RazorConsole/RazorConsole/compare/v0.5.0...v0.6.0"))
  const blogHtml = readFileSync(blog.file, "utf8")
  assert.doesNotMatch(blogHtml, /not yet published|Release preview:/)
  for (const text of ["Mouse events join keyboard input", "@onclick", "@onwheel", "EnableMouseEvents", "off by default", "terminal cells"]) {
    assert.ok(blogHtml.includes(text), `Missing mouse introduction: ${text}`)
  }
  assert.ok(release.links.includes(`${siteBase}/blog/whats-new-in-razorconsole-0-6-0/`))
  assert.ok(blog.links.includes(`${siteBase}/release-notes/v0.6.0/`))
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
