import assert from "node:assert/strict"
import { JSDOM } from "jsdom"
import { pageUrl, productionSite } from "../src/lib/site-paths"

const requested = process.argv[2] ?? productionSite
const site = new URL(requested)
assert.ok(/^https?:$/.test(site.protocol), "Provide an HTTP(S) site URL")
const base = requested.replace(/\/$/, "")
const routes = [
  "/", "/components/table/", "/docs/tutorial/hello-world/", "/blog/hot-reload/",
  "/api/RazorConsole.Components.SpectreTable/", "/release-notes/v0.5.0/", "/blog/choosing-dotnet-tui/",
]
const failures: string[] = []

async function fetchHtml(url: string) {
  const response = await fetch(url, { signal: AbortSignal.timeout(20000) })
  return { response, text: await response.text() }
}

for (const route of routes) {
  const expected = pageUrl(route, base)
  const { response, text } = await fetchHtml(expected)
  const dom = new JSDOM(text)
  const doc = dom.window.document
  const canonicals = [...doc.querySelectorAll('link[rel="canonical"]')]
  const canonical = canonicals[0]?.getAttribute("href")
  const headings = [...doc.querySelectorAll("h1")].map((node) => node.textContent?.trim())
  const noindex = /noindex/i.test(doc.querySelector('meta[name="robots"]')?.getAttribute("content") ?? "")
  if (response.status !== 200 || response.url !== expected || canonical !== expected ||
    canonicals.length !== 1 || headings.length !== 1 || !headings[0] || noindex) {
    failures.push(`${expected}: status=${response.status}, final=${response.url}, canonical=${canonical ?? "missing"}, H1s=${headings.length}, noindex=${noindex}`)
  }
  dom.window.close()
}

const sitemap = await fetchHtml(`${base}/sitemap.xml`)
if (sitemap.response.status !== 200) failures.push(`sitemap status=${sitemap.response.status}`)
else {
  const dom = new JSDOM(sitemap.text, { contentType: "text/xml" })
  const urls = [...dom.window.document.querySelectorAll("loc")].map((node) => node.textContent)
  for (const route of routes) {
    if (!urls.includes(pageUrl(route, base))) failures.push(`Missing sitemap route: ${route}`)
  }
  dom.window.close()
}

const robotsUrl = `${site.origin}/robots.txt`
const robots = await fetchHtml(robotsUrl)
console.log(`Origin-root robots: ${robotsUrl} -> ${robots.response.status}`)
if (robots.response.status === 200) {
  console.log("A root robots file exists. Its rules require owner review; this does not establish crawl/index eligibility.")
} else if (robots.response.status === 404) {
  console.log("No root robots file; absence alone does not block crawling. Do not replace it with a project-path file.")
}
console.log("Read-only HTTP/HTML checks only: no Search Console submission, account verification, or index-status assertion.")
if (failures.length) {
  console.error(failures.join("\n"))
  process.exitCode = 1
} else {
  console.log(`Passed public HTTP/HTML checks for ${routes.length} representative routes and sitemap coverage.`)
}
