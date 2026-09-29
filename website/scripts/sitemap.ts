interface SitemapPage {
  file: string
  canonical?: string
  canonicalCount: number
  indexable: boolean
}

export function sitemapXml(pages: SitemapPage[]): string {
  const urls = new Set<string>()
  for (const page of pages.filter((page) => page.indexable)) {
    if (page.canonicalCount !== 1 || !page.canonical) throw new Error(`Missing or duplicate canonical: ${page.file}`)
    const url = new URL(page.canonical)
    if (!/^https?:$/.test(url.protocol) || !url.pathname.endsWith("/") || url.search || url.hash) {
      throw new Error(`Invalid canonical: ${page.canonical}`)
    }
    if (urls.has(page.canonical)) throw new Error(`Duplicate indexable page: ${page.canonical}`)
    urls.add(page.canonical)
  }
  const escape = (text: string) => text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/"/g, "&quot;")
  // A build timestamp is not a content modification date. Omit lastmod until provenance is available.
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${[...urls].sort().map((url) => `  <url><loc>${escape(url)}</loc></url>`).join("\n")}\n</urlset>\n`
}
