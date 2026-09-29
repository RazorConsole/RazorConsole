import { readdirSync, readFileSync } from "node:fs"
import { join, relative, sep } from "node:path"
import { JSDOM } from "jsdom"

export interface StaticPage {
  file: string
  route: string
  canonical?: string
  indexable: boolean
  title: string
  description: string
  ogUrl?: string
  ogTitle?: string
  ogDescription?: string
  headings: string[]
  links: string[]
  canonicalCount: number
  refresh?: string
}

export function readStaticPages(directory: string): StaticPage[] {
  const pages: StaticPage[] = []
  function walk(folder: string) {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const file = join(folder, entry.name)
      if (entry.isDirectory()) walk(file)
      else if (entry.name === "index.html") {
        const dom = new JSDOM(readFileSync(file, "utf8"))
        const doc = dom.window.document
        const meta = (selector: string) => doc.querySelector(selector)?.getAttribute("content") ?? undefined
        const refresh = meta('meta[http-equiv="refresh" i]')
        const robots = meta('meta[name="robots"]') ?? ""
        const canonical = doc.querySelector('link[rel="canonical"]')?.getAttribute("href") ?? undefined
        pages.push({
          file,
          route: `/${relative(directory, file).split(sep).slice(0, -1).join("/")}/`.replace(/^\/\//, "/"),
          canonical,
          canonicalCount: doc.querySelectorAll('link[rel="canonical"]').length,
          indexable: !refresh && !/\bnoindex\b/i.test(robots),
          title: doc.title,
          description: meta('meta[name="description"]') ?? "",
          ogUrl: meta('meta[property="og:url"]'),
          ogTitle: meta('meta[property="og:title"]'),
          ogDescription: meta('meta[property="og:description"]'),
          headings: [...doc.querySelectorAll("h1")].map((heading) => heading.textContent?.trim() ?? ""),
          links: [...doc.querySelectorAll("a[href]")].map((link) => link.getAttribute("href")!),
          refresh,
        })
        dom.window.close()
      }
    }
  }
  walk(directory)
  if (!pages.length) throw new Error(`No prerendered HTML found in ${directory}; build the website first.`)
  return pages
}
