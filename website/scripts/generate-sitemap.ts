import { resolve } from "node:path"
import { writeFileSync } from "node:fs"
import { readStaticPages } from "./static-pages"
import { sitemapXml } from "./sitemap"

const output = resolve("build/client")
const pages = readStaticPages(output)
writeFileSync(resolve(output, "sitemap.xml"), sitemapXml(pages))
console.log(`[SITEMAP] Generated ${pages.filter((page) => page.indexable).length} canonical URLs`)
