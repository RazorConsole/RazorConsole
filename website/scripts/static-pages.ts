import { readdirSync, readFileSync } from "node:fs"
import { join, relative, sep } from "node:path"
import { defaultTreeAdapter, parse, type DefaultTreeAdapterMap } from "parse5"

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

type HtmlNode = DefaultTreeAdapterMap["node"]
type HtmlElement = DefaultTreeAdapterMap["element"]

function textContent(node: HtmlNode): string {
  if (defaultTreeAdapter.isTextNode(node)) return node.value
  return "childNodes" in node ? node.childNodes.map(textContent).join("") : ""
}

function readPage(file: string, route: string): StaticPage {
  const page: StaticPage = {
    file, route, indexable: true, title: "", description: "",
    headings: [], links: [], canonicalCount: 0,
    canonical: undefined, ogUrl: undefined, ogTitle: undefined, ogDescription: undefined, refresh: undefined,
  }
  const seenMeta = new Set<string>()
  let robots = ""
  let seenTitle = false
  const nodes: HtmlNode[] = [parse(readFileSync(file, "utf8"), { scriptingEnabled: false })]
  while (nodes.length) {
    const node = nodes.pop()!
    if ("tagName" in node && node.namespaceURI === "http://www.w3.org/1999/xhtml") {
      const element: HtmlElement = node
      const attr = (name: string) => element.attrs.find((attribute) => attribute.name === name)?.value
      if (element.tagName === "title" && !seenTitle) {
        page.title = textContent(element).replace(/[\t\n\f\r ]+/g, " ").replace(/^ | $/g, "")
        seenTitle = true
      } else if (element.tagName === "meta") {
        const name = attr("name")?.toLowerCase()
        const property = attr("property")?.toLowerCase()
        const keys = [
          attr("http-equiv")?.toLowerCase() === "refresh" ? "refresh" : undefined,
          name === "robots" || name === "description" ? name : undefined,
          property === "og:url" || property === "og:title" || property === "og:description" ? property : undefined,
        ]
        for (const key of keys) {
          if (!key || seenMeta.has(key)) continue
          seenMeta.add(key)
          const content = attr("content")
          switch (key) {
            case "refresh": page.refresh = content; break
            case "robots": robots = content ?? ""; break
            case "description": page.description = content ?? ""; break
            case "og:url": page.ogUrl = content; break
            case "og:title": page.ogTitle = content; break
            case "og:description": page.ogDescription = content; break
          }
        }
      } else if (element.tagName === "link" && attr("rel")?.toLowerCase() === "canonical") {
        if (page.canonicalCount === 0) page.canonical = attr("href")
        page.canonicalCount++
      } else if (element.tagName === "h1") {
        page.headings.push(textContent(element).trim())
      } else if (element.tagName === "a") {
        const href = attr("href")
        if (href !== undefined) page.links.push(href)
      }
    }
    // Template contents are a separate fragment, not descendants of the document.
    if ("childNodes" in node) {
      for (let index = node.childNodes.length - 1; index >= 0; index--) nodes.push(node.childNodes[index])
    }
  }
  page.indexable = !page.refresh && !/\bnoindex\b/i.test(robots)
  return page
}

export function readStaticPages(directory: string): StaticPage[] {
  const pages: StaticPage[] = []
  function walk(folder: string) {
    for (const entry of readdirSync(folder, { withFileTypes: true })) {
      const file = join(folder, entry.name)
      if (entry.isDirectory()) walk(file)
      else if (entry.name === "index.html") {
        const route = `/${relative(directory, file).split(sep).slice(0, -1).join("/")}/`.replace(/^\/\//, "/")
        pages.push(readPage(file, route))
      }
    }
  }
  walk(directory)
  if (!pages.length) throw new Error(`No prerendered HTML found in ${directory}; build the website first.`)
  return pages
}
