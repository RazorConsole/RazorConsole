export const productionSite = "https://razorconsole.com"
export const legacyProductionSites = [
  "https://razorconsole.github.io/RazorConsole",
  "https://razorconsole.github.io",
]

const fileExtension = /\.(?:avif|css|csv|dat|exe|gif|gz|html|ico|jpeg|jpg|js|json|map|md|mp4|pdf|png|svg|ttf|txt|wasm|webm|webp|woff2?|xml|zip)$/i

/** Normalize HTML routes without modifying external links, files, queries or fragments. */
export function pagePath(value: string): string {
  if (!value || /^(?:[a-z][a-z\d+.-]*:|\/\/|#|\?)/i.test(value)) return value
  const [, pathname, suffix = ""] = value.match(/^([^?#]*)(.*)$/)!
  const route = pathname.replace(/\/index\.html$/i, "/")
  if (fileExtension.test(route)) return value
  return `${route.replace(/\/+$/, "")}/${suffix}`
}

/** Accept both router-relative and already base-prefixed paths. */
export function pageUrl(pathname: string, siteBase: string): string {
  const base = new URL(siteBase)
  const basePath = base.pathname.replace(/\/$/, "")
  let route = pathname.split(/[?#]/)[0]
  if (basePath && (route === basePath || route.startsWith(`${basePath}/`))) {
    route = route.slice(basePath.length)
  }
  return `${base.origin}${basePath}${pagePath(`/${route.replace(/^\/+/, "")}`)}`
}
