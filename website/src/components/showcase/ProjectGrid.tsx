import ImageBanner from "@/components/showcase/ImageBanner"
import InstallDialog from "@/components/showcase/InstallDialog"
import VideoBanner from "@/components/showcase/VideoBanner"
import { Button } from "@/components/ui/Button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/Card"
import type { ShowcaseProject } from "@/data/showcase"
import { Download, Rocket } from "lucide-react"
import { useState } from "react"

export default function ProjectGrid({
  projects,
  emptyMessage,
}: {
  projects: ShowcaseProject[]
  emptyMessage: string
}) {
  const [installProject, setInstallProject] = useState<ShowcaseProject | null>(null)

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
          <Card
            key={project.name}
            className="flex h-full flex-col transition-shadow hover:shadow-lg"
          >
            {project.videoUrl ? (
              <VideoBanner
                src={`${import.meta.env.BASE_URL}${project.videoUrl}`}
                title={project.name}
              />
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
            <CardHeader className={project.installCommands ? "pb-3" : undefined}>
              <CardTitle className="text-xl">{project.name}</CardTitle>
            </CardHeader>
            <CardContent className="flex flex-1 flex-col">
              <CardDescription className="flex-1">{project.description}</CardDescription>
              {project.installCommands && project.installCommands.length > 0 && (
                <Button
                  type="button"
                  className="mt-6 w-full gap-2 sm:w-auto sm:self-start"
                  onClick={() => setInstallProject(project)}
                >
                  <Download className="h-4 w-4" aria-hidden="true" />
                  Install
                </Button>
              )}
              {!project.installCommands && (
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
              )}
            </CardContent>
          </Card>
        )
      })}
      {installProject && (
        <InstallDialog project={installProject} onClose={() => setInstallProject(null)} />
      )}
    </div>
  )
}
