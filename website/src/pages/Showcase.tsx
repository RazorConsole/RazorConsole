import ImageBanner from "@/components/showcase/ImageBanner"
import VideoBanner from "@/components/showcase/VideoBanner"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/Card"
import { showcaseProjects } from "@/data/showcase"
import { getFullSitePath } from "@/lib/utils"
import { Rocket } from "lucide-react"
import type { MetaFunction } from "react-router"

export const meta: MetaFunction = ({ matches, location }) => {
  const rootMeta = matches.find((m) => m.id === "root")?.meta || []
  const pageUrl = `${getFullSitePath()}${location.pathname}`

  return [
    ...rootMeta,
    { property: "og:url", content: pageUrl },
    { title: "Showcase | RazorConsole" },
    {
      name: "description",
      content: "Discover amazing projects and terminal interfaces built by the community using RazorConsole.",
    },
  ]
}

export default function Showcase() {
  const getProjectUrl = (project: (typeof showcaseProjects)[0]) => {
    if (project.github) return `https://github.com/${project.github}`
    if (project.website) return project.website
    return undefined
  }

  return (
    <div className="min-h-screen bg-linear-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="container mx-auto px-4 py-16">
        <div className="mb-12 text-center">
          <h1 className="mb-4 text-4xl font-bold tracking-tight text-slate-900 dark:text-slate-50">
            Showcase
          </h1>
          <p className="mx-auto max-w-2xl text-lg text-slate-600 dark:text-slate-300">
            Discover projects built with RazorConsole
          </p>
        </div>

        {showcaseProjects.length > 0 ? (
          <div className="mx-auto grid max-w-6xl grid-cols-1 gap-8 md:grid-cols-2">
            {showcaseProjects.map((project) => {
              const projectUrl = getProjectUrl(project)
              return (
                <Card key={project.name} className="flex h-full flex-col transition-shadow hover:shadow-lg">
                  {project.videoUrl ? (
                    <VideoBanner
                      src={`${import.meta.env.BASE_URL}${project.videoUrl}`}
                      title={project.name}
                    />
                  ) : project.imageUrls && project.imageUrls.length > 0 ? (
                    <ImageBanner imageUrls={project.imageUrls} alt={project.name} />
                  ) : null}
                  <CardHeader>
                    <CardTitle className="text-xl">{project.name}</CardTitle>
                  </CardHeader>
                  <CardContent className="flex flex-1 flex-col">
                    <CardDescription className="flex-1">{project.description}</CardDescription>
                    {project.installCommands && project.installCommands.length > 0 && (
                      <div className="mt-5 space-y-3">
                        <p className="text-sm font-semibold text-slate-900 dark:text-slate-100">Install</p>
                        {project.installCommands.map((install) => (
                          <div key={install.label}>
                            <p className="mb-1 text-xs font-medium text-slate-500 dark:text-slate-400">
                              {install.label}
                            </p>
                            <code className="block overflow-x-auto rounded-md bg-slate-950 px-3 py-2 text-xs whitespace-nowrap text-slate-100">
                              {install.command}
                            </code>
                          </div>
                        ))}
                      </div>
                    )}
                    <div className="mt-5 flex flex-wrap gap-3 text-sm font-semibold">
                      {projectUrl && (
                        <a
                          href={projectUrl}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="text-blue-600 hover:underline dark:text-blue-400"
                        >
                          View project
                        </a>
                      )}
                      {project.downloadUrl && (
                        <a
                          href={project.downloadUrl}
                          target="_blank"
                          rel="noopener noreferrer"
                          className="text-blue-600 hover:underline dark:text-blue-400"
                        >
                          Download binaries
                        </a>
                      )}
                    </div>
                  </CardContent>
                </Card>
              )
            })}
          </div>
        ) : (
          <div className="py-12 text-center">
            <Rocket className="mx-auto mb-4 h-16 w-16 text-slate-300 dark:text-slate-600" />
            <p className="mb-2 text-lg text-slate-600 dark:text-slate-400">No projects showcased yet.</p>
            <p className="text-sm text-slate-500 dark:text-slate-500">Be the first to add your project!</p>
          </div>
        )}
      </div>
    </div>
  )
}
