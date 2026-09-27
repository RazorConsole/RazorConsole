/* eslint-disable react-refresh/only-export-components */
import ProjectGrid from "@/components/showcase/ProjectGrid"
import { officialApps } from "@/data/official-apps"
import { getFullSitePath } from "@/lib/utils"
import type { MetaFunction } from "react-router"

export const meta: MetaFunction = ({ matches, location }) => {
  const rootMeta = matches.find((m) => m.id === "root")?.meta || []
  return [
    ...rootMeta,
    { property: "og:url", content: `${getFullSitePath()}${location.pathname}` },
    { title: "Gallery | RazorConsole" },
    {
      name: "description",
      content: "Discover official applications maintained by the RazorConsole project.",
    },
  ]
}

export default function Gallery() {
  return (
    <div className="min-h-screen bg-linear-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="container mx-auto px-4 py-16">
        <div className="mb-12 text-center">
          <h1 className="text-4xl font-bold tracking-tight text-slate-900 dark:text-slate-50">
            RazorConsole Gallery
          </h1>
        </div>
        <ProjectGrid
          projects={officialApps}
          emptyMessage="No official applications are available yet."
        />
      </div>
    </div>
  )
}
