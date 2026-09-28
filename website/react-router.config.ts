import type { Config } from "@react-router/dev/config"
import { components } from "./src/data/components"
import { apiItems } from "./src/data/api-docs"
import { docTopicIds, releaseNoteIds } from "./src/data/docs-ids"

export default {
  appDirectory: "src",
  ssr: false,
  basename: process.env.VITE_ROUTER_BASENAME || "/",
  async prerender({ getStaticPaths }) {
    // For dynamic routes, that have indexes
    const dynamicPathIndexes = ["/docs", "/blog", "/api"]

    const componentPaths = components.map((comp) => `/components/${comp.name.toLowerCase()}`)

    const apiPaths = Object.keys(apiItems).map((uid) => `/api/${encodeURIComponent(uid)}`)

    const legacyDocsPaths = [...docTopicIds, ...releaseNoteIds].map((item) => `/docs/${item.id}`)

    const blogPaths = docTopicIds
      .filter((item) => item.id !== "quick-start")
      .map((item) => `/blog/${item.id}`)

    const tutorialSlugs = [
      "hello-world",
      "state-and-events",
      "text-input-and-focus",
      "mouse-events",
      "widget-layout-and-resize",
      "routing",
      "async-work",
      "complete-app",
    ]

    const tutorialPaths = tutorialSlugs.map((slug) => `/docs/tutorial/${slug}`)
    const legacyTutorialPaths = tutorialSlugs.map((slug) => `/tutorial/${slug}`)
    const releasePaths = releaseNoteIds.map((item) => `/release-notes/${item.id}`)

    return [
      ...getStaticPaths(),
      ...dynamicPathIndexes,
      ...componentPaths,
      ...apiPaths,
      ...legacyDocsPaths,
      ...blogPaths,
      ...tutorialPaths,
      ...legacyTutorialPaths,
      ...releasePaths,
    ]
  },
} satisfies Config
