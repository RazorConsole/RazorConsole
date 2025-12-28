import ImageBanner from "@/components/showcase/ImageBanner"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/Card"
import { showcaseProjects } from "@/data/showcase"
import { Rocket } from "lucide-react"

export default function Showcase() {
  const getProjectUrl = (project: (typeof showcaseProjects)[0]) => {
    if (project.github) return `https://github.com/${project.github}`
    if (project.website) return project.website
    return undefined
  }

  return (
    <div className="min-h-screen bg-gradient-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
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
                <a
                  key={project.name}
                  href={projectUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="block transition-transform hover:scale-[1.02]"
                >
                  <Card className="flex h-full cursor-pointer flex-col transition-shadow hover:shadow-lg">
                    {project.imageUrls && project.imageUrls.length > 0 && (
                      <ImageBanner imageUrls={project.imageUrls} alt={project.name} />
                    )}
                    <CardHeader>
                      <CardTitle className="text-xl">{project.name}</CardTitle>
                    </CardHeader>
                    <CardContent className="flex flex-1 flex-col">
                      <CardDescription className="flex-1">{project.description}</CardDescription>
                    </CardContent>
                  </Card>
                </a>
              )
            })}
          </div>
        ) : (
          <div className="py-12 text-center">
            <Rocket className="mx-auto mb-4 h-16 w-16 text-slate-300 dark:text-slate-600" />
            <p className="mb-2 text-lg text-slate-600 dark:text-slate-400">
              No projects showcased yet.
            </p>
            <p className="text-sm text-slate-500 dark:text-slate-500">
              Be the first to add your project!
            </p>
          </div>
        )}
      </div>
    </div>
  )
}
