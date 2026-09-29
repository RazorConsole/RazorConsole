import assert from "node:assert/strict"
import { test } from "node:test"
import { readFileSync } from "node:fs"
import { pagePath, pageUrl, productionSite } from "../src/lib/site-paths.ts"
import { apiDescription, ensurePageHeading, sanitizeDocText } from "../src/lib/doc-utils.ts"
import { docTopicIds } from "../src/data/docs-ids.ts"
import { sitemapXml } from "./sitemap.ts"
import { documentHref } from "../src/lib/document-links.ts"

test("page URLs normalize base paths, index aliases, dotted API symbols and slashes", () => {
  for (const route of ["/", "/components/table", "/docs/tutorial/hello-world", "/blog/hot-reload", "/api/RazorConsole.Components.SpectreTable", "/release-notes/v0.5.0"]) {
    const expected = `${productionSite}${pagePath(route)}`
    assert.equal(pageUrl(route, productionSite), expected)
    assert.equal(pageUrl(`/RazorConsole${route}`, productionSite), expected)
    assert.equal(pageUrl(`${pagePath(route)}index.html?q=1#section`, productionSite), expected)
  }
  assert.equal(pageUrl("/api/A.B", "https://pr-12.razorconsole.pages.dev/"), "https://pr-12.razorconsole.pages.dev/api/A.B/")
  assert.equal(pageUrl("/components/table", "http://localhost:5173/RazorConsole/"), "http://localhost:5173/RazorConsole/components/table/")
  assert.equal(pagePath("/blog/hot-reload?view=all#setup"), "/blog/hot-reload/?view=all#setup")
})

test("files, external links and in-page navigation are not rewritten as directories", () => {
  for (const value of ["/llms.txt", "/sitemap.xml", "/assets/main.js", "/guide.md#example", "/image.png?width=2", "#section", "?q=1", "https://example.com/a", "//example.com/a", "mailto:hi@example.com"]) {
    assert.equal(pagePath(value), value)
  }
})

test("absolute legacy documentation links resolve to canonical local routes in production and previews", () => {
  for (const base of ["/", "/RazorConsole/"]) {
    const prefix = base.replace(/\/$/, "")
    assert.equal(documentHref(`${productionSite}/components/Align`, base), `${prefix}/components/align/`)
    assert.equal(documentHref(`${productionSite}/components`, base), `${prefix}/components/`)
    assert.equal(documentHref(`${productionSite}/docs/native-aot#publish`, base), `${prefix}/blog/native-aot/#publish`)
    assert.equal(documentHref(`${productionSite}/raw/guide.md`, base), `${prefix}/raw/guide.md`)
    assert.equal(documentHref(`${productionSite}/components/Table.md`, base), `${prefix}/components/Table.md`)
    assert.equal(documentHref(`${productionSite}?q=table#preview`, base), `${prefix}/?q=table#preview`)
    assert.equal(documentHref(`${productionSite}-other/components/Align`, base), `${productionSite}-other/components/Align`)
    assert.equal(documentHref("https://example.com/components/Align", base), "https://example.com/components/Align")
  }
})

test("API descriptions preserve readable xrefs and use truthful symbol-specific fallbacks", () => {
  assert.equal(sanitizeDocText('Uses <xref href="RazorConsole.Input.KeyboardEventArgs" /> and <xref href="System.String">text</xref>.'), "Uses KeyboardEventArgs and text.")
  assert.equal(sanitizeDocText('<p>A &lt;T&gt; &amp; B &#x41;.</p>'), "A <T> & B A.")
  assert.equal(apiDescription({ name: "Panel", summary: "<p>Renders a <b>panel</b>.</p>" }), "Panel: Renders a panel.")
  const first = apiDescription({ name: "Panel", type: "Class", namespace: "RazorConsole.Components" })
  const second = apiDescription({ name: "TextInput", type: "Class", namespace: "RazorConsole.Components" })
  assert.notEqual(first, second)
  assert.match(first, /Panel class in RazorConsole.Components/)
  assert.doesNotMatch(first, /Full API reference/)
})

test("legacy Markdown gets one title without changing existing titles or heading anchors", () => {
  assert.equal(ensurePageHeading("### Hot Reload\n\n## Setup", "Fallback"), "# Hot Reload\n\n## Setup")
  assert.equal(ensurePageHeading("# Existing\n\n## Setup", "Fallback"), "# Existing\n\n## Setup")
  assert.equal(ensurePageHeading("Some text", "Fallback"), "# Fallback\n\nSome text")
  assert.equal(ensurePageHeading("```cs\n# not a heading\n```\n### Title", "Fallback"), "```cs\n# not a heading\n```\n# Title")
  assert.equal(ensurePageHeading("~~~\n# not a heading\n~~~\n### Title", "Fallback"), "~~~\n# not a heading\n~~~\n# Title")
})

test("all registered blog sources have one normalized page title", () => {
  for (const topic of docTopicIds.filter((entry) => entry.id !== "quick-start")) {
    const file = new URL(`../../${topic.filePath}`, import.meta.url)
    const normalized = ensurePageHeading(readFileSync(file, "utf8"), topic.title)
    const prose = normalized.replace(/^(`{3,}|~{3,})[\s\S]*?^\1\s*$/gm, "")
    assert.equal(prose.match(/^\uFEFF?#\s+.+/gm)?.length, 1, topic.id)
  }
})

test("sitemap serialization excludes nonindexable pages and rejects invalid or duplicate canonicals", () => {
  const page = { file: "index.html", canonical: `${productionSite}/`, canonicalCount: 1, indexable: true }
  const xml = sitemapXml([page, { ...page, file: "redirect/index.html", indexable: false }])
  assert.equal(xml.match(/<loc>/g)?.length, 1)
  assert.ok(xml.includes(`<loc>${productionSite}/</loc>`))
  assert.doesNotMatch(xml, /lastmod|priority|changefreq|index\.html/)
  assert.throws(() => sitemapXml([page, page]), /Duplicate indexable/)
  assert.throws(() => sitemapXml([{ ...page, canonicalCount: 0 }]), /Missing or duplicate/)
  for (const canonical of [`${productionSite}/api`, `${productionSite}/?q=1`, `${productionSite}/#title`, "file:///docs/"]) {
    assert.throws(() => sitemapXml([{ ...page, canonical }]), /Invalid canonical/)
  }
})

test("the .NET TUI comparison blog post has a single H1 and links to registered content", () => {
  const allowed = new Set([
    "/components/", "/api/",
    ...docTopicIds.map((topic) => `/blog/${topic.id}/`),
    ...["hello-world", "state-and-events", "text-input-and-focus", "mouse-events", "widget-layout-and-resize", "routing", "async-work", "complete-app"].map((slug) => `/docs/tutorial/${slug}/`),
  ])
  const content = readFileSync(new URL("../src/docs/choosing-dotnet-tui.md", import.meta.url), "utf8")
  assert.equal(content.match(/^# .+/gm)?.length, 1)
  for (const link of content.matchAll(/\]\((\/[^)]+)\)/g)) {
    assert.ok(allowed.has(link[1]), `Unknown link ${link[1]}`)
  }
  for (const term of ["Razor", "keyboard", "mouse", "NativeAOT"]) assert.ok(content.includes(term), term)
  assert.doesNotMatch(content, /\bInk\b/)
})
