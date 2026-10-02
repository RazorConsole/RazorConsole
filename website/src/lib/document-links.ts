import { docTopicIds, releaseNoteIds } from "../data/docs-ids.ts"
import { pagePath, productionSite } from "./site-paths.ts"

export function documentHref(href: string | undefined, baseUrl: string, siteUrl?: string): string | undefined {
  if (!href) return href
  const base = baseUrl.replace(/\/$/, "")
  let route = href
  for (const site of [productionSite, siteUrl ? `${siteUrl.replace(/\/$/, "")}${base}` : undefined]) {
    if (site && (route === site || ["/", "?", "#"].some((suffix) => route.startsWith(`${site}${suffix}`)))) {
      route = route.slice(site.length) || "/"
      if (route.startsWith("?") || route.startsWith("#")) route = `/${route}`
      break
    }
  }
  if (!route.startsWith("/") || route.startsWith("//")) return href
  if (base && route.startsWith(`${base}/`)) route = route.slice(base.length)
  route = route.replace(/^\/components\/([^/.?#]+)(?=\/?(?:[?#]|$))/, (_, slug: string) => `/components/${slug.toLowerCase()}`)
  route = route.replace(/^\/docs#([^#?]+)$/, "/blog/$1")
  route = route.replace(/^\/docs\/([^/?#]+)(?=\/?(?:[?#]|$))/, (match, id: string) => {
    if (id === "quick-start") return "/docs/tutorial/hello-world"
    if (docTopicIds.some((topic) => topic.id === id)) return `/blog/${id}`
    if (releaseNoteIds.some((note) => note.id === id)) return `/release-notes/${id}`
    return match
  })
  return `${base}${pagePath(route)}`
}
