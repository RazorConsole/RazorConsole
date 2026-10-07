import assert from "node:assert/strict"
import { existsSync, readFileSync, readdirSync } from "node:fs"
import { join, relative, resolve, sep } from "node:path"
import { test } from "node:test"
import { JSDOM } from "jsdom"
import { legacyNotFoundHtml, legacyProjectPath, legacyRedirectHtml, redirectLocation } from "./legacy-redirects"
import { readStaticPages } from "./static-pages"

const rootSite = "https://razorconsole.github.io"

test("redirect HTML sends old routes directly to root canonicals", () => {
  const target = `${rootSite}/docs/tutorial/hello-world/`
  const html = legacyRedirectHtml(target)
  const dom = new JSDOM(html, { url: `${rootSite}${legacyProjectPath}/docs/tutorial/hello-world/?mode=full#setup` })
  const document = dom.window.document
  assert.equal(document.querySelector('meta[name="robots"]')?.getAttribute("content"), "noindex, follow")
  assert.equal(document.querySelector('link[rel="canonical"]')?.getAttribute("href"), target)
  assert.equal(document.querySelector('meta[http-equiv="refresh"]')?.getAttribute("content"), `0; url=${target}`)
  assert.match(document.querySelector("script")?.textContent ?? "", /location\.search \+ location\.hash/)
  assert.doesNotMatch(html, /razorconsole\.github\.io\/RazorConsole/)
  assert.equal(document.querySelectorAll("h1").length, 0)
  assert.equal(redirectLocation(target, "?mode=full", "#setup"), `${target}?mode=full#setup`)
  dom.window.close()
})

test("fallback removes only the legacy project prefix and preserves browser URL state", () => {
  const html = legacyNotFoundHtml(rootSite)
  assert.match(html, /replace\(\/\^\\\/RazorConsole/)
  assert.match(html, /location\.search \+ location\.hash/)
  assert.doesNotMatch(html, /http-equiv="refresh"/)
  assert.doesNotMatch(html, /razorconsole\.github\.io\/RazorConsole/)
})

test("generated artifact has one redirect for every current root canonical", () => {
  const source = resolve(process.env.WEBSITE_BUILD_DIR || "build/client")
  const output = resolve(process.env.LEGACY_REDIRECT_OUTPUT || "build/legacy-redirects")
  if (!existsSync(output)) return

  const pages = readStaticPages(source).filter((page) => page.indexable)
  const redirectFiles: string[] = []
  const walk = (folder: string) => {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const path = join(folder, entry.name)
      if (entry.isDirectory()) walk(path)
      else if (entry.name === "index.html") redirectFiles.push(path)
    }
  }
  walk(output)
  assert.equal(redirectFiles.length, pages.length)

  const targets = new Set(pages.map((page) => page.canonical))
  for (const file of redirectFiles) {
    const html = readFileSync(file, "utf8")
    const dom = new JSDOM(html)
    const canonical = dom.window.document.querySelector('link[rel="canonical"]')?.getAttribute("href")
    assert.ok(targets.delete(canonical), `${relative(output, file).split(sep).join("/")}: ${canonical}`)
    assert.equal(dom.window.document.querySelector('meta[name="robots"]')?.getAttribute("content"), "noindex, follow")
    assert.match(dom.window.document.querySelector('meta[http-equiv="refresh"]')?.getAttribute("content") ?? "", /^0; url=https:\/\/razorconsole\.github\.io\//)
    assert.equal(dom.window.document.querySelectorAll("h1").length, 0)
    dom.window.close()
  }
  assert.deepEqual([...targets], [])
  assert.equal(readFileSync(join(output, "sitemap.xml"), "utf8"), readFileSync(join(source, "sitemap.xml"), "utf8"))
  assert.equal(
    readFileSync(join(output, "googleddbc334a439061f3.html"), "utf8"),
    readFileSync(resolve("public/googleddbc334a439061f3.html"), "utf8"),
  )
})
