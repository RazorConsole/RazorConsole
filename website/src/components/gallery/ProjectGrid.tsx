import ImageBanner from "@/components/ui/ImageBanner"
import InstallDialog from "@/components/gallery/InstallDialog"
import VideoBanner from "@/components/ui/VideoBanner"
import { Button } from "@/components/ui/Button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/Card"
import type { ShowcaseProject } from "@/data/showcase"
import { Download, Rocket } from "lucide-react"
import { useState } from "react"
import { Link } from "@/components/ui/SiteLink"

export default function ProjectGrid({
  projects,
  emptyMessage,
  detailPathPrefix,
}: {
  projects: ShowcaseProject[]
  emptyMessage: string
  detailPathPrefix?: string
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
        const detailUrl =
          detailPathPrefix && project.slug ? `${detailPathPrefix}/${project.slug}` : undefined
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
              <CardTitle className="text-xl">
                {detailUrl ? (
                  <Link className="hover:text-violet-700 dark:hover:text-violet-300" to={detailUrl}>
                    {project.name}
                  </Link>
                ) : (
                  project.name
                )}
              </CardTitle>
            </CardHeader>
            <CardContent className="flex flex-1 flex-col">
              <CardDescription className="flex-1">{project.description}</CardDescription>
              {project.installCommands && project.installCommands.length > 0 && (
                <div className="mt-6 flex flex-wrap gap-3">
                  {detailUrl && (
                    <Link
                      className="inline-flex h-10 items-center justify-center rounded-md border border-slate-200 bg-white px-4 py-2 text-sm font-medium transition-colors hover:bg-slate-100 dark:border-slate-800 dark:bg-slate-950 dark:hover:bg-slate-800"
                      to={detailUrl}
                    >
                      View details
                    </Link>
                  )}
                  <Button
                    type="button"
                    className="gap-2"
                    onClick={() => setInstallProject(project)}
                  >
                    <Download className="h-4 w-4" aria-hidden="true" />
                    Install
                  </Button>
                </div>
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
