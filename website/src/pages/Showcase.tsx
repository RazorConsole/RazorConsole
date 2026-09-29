import ProjectGrid from "@/components/showcase/ProjectGrid"
import { showcaseProjects } from "@/data/showcase"
import { getPageUrl } from "@/lib/utils"
import type { MetaFunction } from "react-router"

export const meta: MetaFunction = ({ matches, location }) => {
  const rootMeta = matches.find((m) => m.id === "root")?.meta || []
  return [
    ...rootMeta,
    { property: "og:url", content: getPageUrl(location.pathname) },
    { title: "Showcase | RazorConsole" },
    {
      name: "description",
      content: "Discover projects and terminal interfaces built by the RazorConsole community.",
    },
  ]
}

export default function Showcase() {
  return (
    <div className="min-h-screen bg-linear-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="container mx-auto px-4 py-16">
        <div className="mb-12 text-center">
          <h1 className="mb-4 text-4xl font-bold tracking-tight text-slate-900 dark:text-slate-50">
            Showcase
          </h1>
          <p className="mx-auto max-w-2xl text-lg text-slate-600 dark:text-slate-300">
            Projects built by the RazorConsole community
          </p>
        </div>
        <ProjectGrid projects={showcaseProjects} emptyMessage="No community projects showcased yet." />
      </div>
    </div>
  )
}
