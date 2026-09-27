import ImageBanner from "@/components/showcase/ImageBanner"
import VideoBanner from "@/components/showcase/VideoBanner"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/Card"
import type { ShowcaseProject } from "@/data/showcase"
import { Rocket } from "lucide-react"

export default function ProjectGrid({
  projects,
  emptyMessage,
}: {
  projects: ShowcaseProject[]
  emptyMessage: string
}) {
  const getProjectUrl = (project: ShowcaseProject) => {
    if (project.github) return `https://github.com/${project.github}`
    return project.website
  }

  if (projects.length === 0) {
    return (
      <div className="py-12 text-center">
        <Rocket className="mx-auto mb-4 h-16 w-16 text-slate-300 dark:text-slate-600" />
        <p className="text-lg text-slate-600 dark:text-slate-400">{emptyMessage}</p>
      </div>
    )
  }

  return (
    <div className="mx-auto grid max-w-6xl grid-cols-1 gap-8 md:grid-cols-2">
      {projects.map((project) => {
        const projectUrl = getProjectUrl(project)
        return (
          <Card key={project.name} className="flex h-full flex-col transition-shadow hover:shadow-lg">
            {project.videoUrl ? (
              <VideoBanner src={`${import.meta.env.BASE_URL}${project.videoUrl}`} title={project.name} />
            ) : project.imageUrls && project.imageUrls.length > 0 ? (
              <ImageBanner imageUrls={project.imageUrls} alt={project.name} />
            ) : (
              <div className="flex h-72 items-center justify-center rounded-t-lg bg-linear-to-br from-slate-950 via-slate-900 to-violet-950">
                <img
                  src={`${import.meta.env.BASE_URL}razorconsole-icon.svg`}
                  alt=""
                  className="h-24 w-24 drop-shadow-2xl"
                />
              </div>
            )}
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
  )
}
