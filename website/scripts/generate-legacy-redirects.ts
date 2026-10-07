import { copyFileSync, mkdirSync, rmSync, writeFileSync } from "node:fs"
import { resolve } from "node:path"
import { generateLegacyRedirects, legacyNotFoundHtml } from "./legacy-redirects"
import { readStaticPages } from "./static-pages"

const source = resolve(process.env.WEBSITE_BUILD_DIR || "build/client")
const output = resolve(process.env.LEGACY_REDIRECT_OUTPUT || "build/legacy-redirects")
const rootSite = process.env.VITE_SITE_URL || "https://razorconsole.com"

rmSync(output, { recursive: true, force: true })
mkdirSync(output, { recursive: true })

const count = generateLegacyRedirects(readStaticPages(source), output, rootSite)
writeFileSync(resolve(output, "404.html"), legacyNotFoundHtml(rootSite))
writeFileSync(resolve(output, ".nojekyll"), "")
copyFileSync(resolve("public/googleddbc334a439061f3.html"), resolve(output, "googleddbc334a439061f3.html"))

console.log(`[REDIRECTS] Generated ${count} route redirects from ${source} into ${output}`)
