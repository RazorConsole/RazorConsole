import assert from "node:assert/strict"
import { spawnSync } from "node:child_process"
import { mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs"
import { tmpdir } from "node:os"
import { dirname, join } from "node:path"
import { test } from "node:test"
import { fileURLToPath } from "node:url"
import { JSDOM } from "jsdom"
import { readStaticPages } from "./static-pages"
import { sitemapXml } from "./sitemap"

const fixtures = [
  `<!doctype html><html><head>
    <title>  A &amp; B
      website &lt;title&gt; </title>
    <meta name="description" content="Read &quot;this&quot; &amp; that">
    <meta property="og:url" content="https://example.com/project/page/">
    <meta property="og:title" content="Social &amp; title">
    <meta property="og:description" content="Social description">
    <link rel="CANONICAL" href="https://example.com/project/page/">
    <link rel="canonical" href="/duplicate/">
    </head><body><h1> Hello <em>world &amp; friends</em><!-- ignored --> </h1><h1></h1>
    <a href="/one/?a=1&amp;b=2">One</a><a href="">Empty</a><a>No href</a>
    <template><h1>Hidden</h1><a href="/hidden/">Hidden</a></template>
    <script>const html = '<h1>Not markup</h1><a href="/script/">link</a>';</script>
    </body></html>`,
  `<title>Redirect</title><meta HTTP-EQUIV="ReFrEsH" content="0; URL='/target/'">
    <meta name="robots" content="INDEX, NoIndex, follow"><link rel="canonical" href="/redirect/">
    <h1>Redirect</h1>`,
  `<title>First</title><title>Second</title>
    <meta name="description"><meta name="description" content="Not the first">
    <meta name="robots"><meta name="robots" content="noindex">
    <meta property="og:title"><meta property="og:title" content="Not the first">
    <link rel="canonical"><link rel="canonical" href="/not-the-first/">
    <meta http-equiv="refresh" content=""><h1> A&nbsp;B </h1>`,
  `<meta name="description" property="og:description" content="Shared">
    <meta name="robots" content="noindex"><h1>Unclosed <em>heading</h1><a href="/target/">Link`,
  `<meta NAME="DESCRIPTION" content="Uppercase"><meta NAME="ROBOTS" content="NOINDEX">
    <meta PROPERTY="OG:TITLE" content="Uppercase property"><h1>Case sensitivity</h1>`,
  `<h1>No metadata</h1><a href="/plain/">Plain</a>`,
  `<html><head><noscript><meta name="description" content="Without scripts"></noscript></head>
    <body><noscript><h1>Without scripts</h1><a href="/fallback/">Fallback</a></noscript></body></html>`,
]

test("static metadata extraction matches JSDOM without creating browser windows", () => {
  const directory = mkdtempSync(join(tmpdir(), "razorconsole-static-pages-"))
  try {
    fixtures.forEach((html, index) => {
      const folder = join(directory, "project", `page-${index}`)
      mkdirSync(folder, { recursive: true })
      writeFileSync(join(folder, "index.html"), html)
    })
    writeFileSync(join(directory, "404.html"), "<h1>Not a page</h1>")
    const pages = readStaticPages(directory)
    assert.equal(pages.length, fixtures.length)
    for (const page of pages) {
      assert.match(page.route, /^\/project\/page-\d+\/$/)
      const dom = new JSDOM(readFileSync(page.file, "utf8"))
      try {
        const doc = dom.window.document
        const meta = (selector: string) => doc.querySelector(selector)?.getAttribute("content") ?? undefined
        const refresh = meta('meta[http-equiv="refresh" i]')
        assert.deepEqual(page, {
          file: page.file,
          route: page.route,
          canonical: doc.querySelector('link[rel="canonical"]')?.getAttribute("href") ?? undefined,
          canonicalCount: doc.querySelectorAll('link[rel="canonical"]').length,
          indexable: !refresh && !/\bnoindex\b/i.test(meta('meta[name="robots"]') ?? ""),
          title: doc.title,
          description: meta('meta[name="description"]') ?? "",
          ogUrl: meta('meta[property="og:url"]'),
          ogTitle: meta('meta[property="og:title"]'),
          ogDescription: meta('meta[property="og:description"]'),
          headings: [...doc.querySelectorAll("h1")].map((heading) => heading.textContent?.trim() ?? ""),
          links: [...doc.querySelectorAll("a[href]")].map((link) => link.getAttribute("href")!),
          refresh,
        }, page.route)
      } finally {
        dom.window.close()
      }
    }
  } finally {
    rmSync(directory, { recursive: true, force: true })
  }
})

test("static reader preserves the root route and reports missing prerendered HTML", () => {
  const directory = mkdtempSync(join(tmpdir(), "razorconsole-static-root-"))
  try {
    assert.throws(() => readStaticPages(directory), /No prerendered HTML found/)
    writeFileSync(join(directory, "index.html"), "<title>Home</title>")
    assert.equal(readStaticPages(directory)[0].route, "/")
  } finally {
    rmSync(directory, { recursive: true, force: true })
  }
})

test("sitemap generation processes many large pages within a 96 MiB heap", () => {
  const directory = mkdtempSync(join(tmpdir(), "razorconsole-sitemap-memory-"))
  const output = join(directory, "build", "client")
  const count = 200
  // Prerendered pages embed hydration data that is irrelevant to sitemap extraction.
  const hydration = "x".repeat(256 * 1024)
  try {
    for (let index = 0; index < count; index++) {
      const folder = join(output, `page-${index}`)
      mkdirSync(folder, { recursive: true })
      writeFileSync(join(folder, "index.html"),
        `<title>Page ${index}</title><link rel="canonical" href="https://example.com/page-${index}/">
        <h1>Page ${index}</h1><script type="application/json">"${hydration}"</script>`)
    }
    const script = fileURLToPath(new URL("./generate-sitemap.ts", import.meta.url))
    const tsx = import.meta.resolve("tsx")
    const result = spawnSync(process.execPath, ["--max-old-space-size=96", "--import", tsx, script], {
      cwd: directory,
      env: { ...process.env, NODE_OPTIONS: "", TSX_TSCONFIG_PATH: join(dirname(script), "..", "tsconfig.node.json") },
      encoding: "utf8",
      timeout: 60_000,
    })
    assert.ifError(result.error)
    assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`)
    assert.match(result.stdout, new RegExp(`Generated ${count} canonical URLs`))
    const xml = readFileSync(join(output, "sitemap.xml"), "utf8")
    assert.equal(xml, sitemapXml(readStaticPages(output)))
    assert.equal((xml.match(/<loc>/g) ?? []).length, count)
  } finally {
    rmSync(directory, { recursive: true, force: true })
  }
})
