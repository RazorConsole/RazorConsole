import { useState, useEffect } from "react"
import ImageBanner from "@/components/ui/ImageBanner"
import VideoBanner from "@/components/ui/VideoBanner"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/Card"
import { showcaseProjects } from "@/data/showcase"
import { getPageUrl } from "@/lib/utils"
import { Rocket, X, ChevronLeft, ChevronRight } from "lucide-react"
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

type ModalState = {
  images: string[]
  currentIndex: number
  title: string
}

export default function Showcase() {
  const [modalMedia, setModalMedia] = useState<ModalState | null>(null)

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (!modalMedia) return

      if (e.key === "Escape") {
        setModalMedia(null)
      } else if (e.key === "ArrowLeft" && modalMedia.images.length > 1) {
        setModalMedia((prev) =>
          prev
            ? {
                ...prev,
                currentIndex: (prev.currentIndex - 1 + prev.images.length) % prev.images.length,
              }
            : null,
        )
      } else if (e.key === "ArrowRight" && modalMedia.images.length > 1) {
        setModalMedia((prev) =>
          prev
            ? {
                ...prev,
                currentIndex: (prev.currentIndex + 1) % prev.images.length,
              }
            : null,
        )
      }
    }

    if (modalMedia) {
      document.body.style.overflow = "hidden"
      window.addEventListener("keydown", handleKeyDown)
    }
    return () => {
      document.body.style.overflow = ""
      window.removeEventListener("keydown", handleKeyDown)
    }
  }, [modalMedia])

  const getProjectUrl = (project: (typeof showcaseProjects)[0]) => {
    if (project.github) return `https://github.com/${project.github}`
    if (project.website) return project.website
    return undefined
  }

  const handleNextImage = () => {
    if (!modalMedia) return
    setModalMedia({
      ...modalMedia,
      currentIndex: (modalMedia.currentIndex + 1) % modalMedia.images.length,
    })
  }

  const handlePrevImage = () => {
    if (!modalMedia) return
    setModalMedia({
      ...modalMedia,
      currentIndex: (modalMedia.currentIndex - 1 + modalMedia.images.length) % modalMedia.images.length,
    })
  }

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

        {showcaseProjects.length > 0 ? (
          <div className="mx-auto flex max-w-5xl flex-col gap-8">
            {showcaseProjects.map((project) => {
              const projectUrl = getProjectUrl(project)
              const hasMedia = Boolean(project.videoUrl || (project.imageUrls && project.imageUrls.length > 0))

              return (
                <Card
                  key={project.name}
                  className="flex flex-col overflow-hidden transition-shadow hover:shadow-lg md:flex-row"
                >
                  {/* Media section */}
                  {hasMedia && (
                    <div className="flex shrink-0 items-center justify-center bg-slate-950 p-3 md:w-1/2 lg:w-5/12 aspect-video md:aspect-auto">
                      <div className="flex h-full w-full items-center justify-center">
                        {project.videoUrl ? (
                          <VideoBanner
                            src={`${import.meta.env.BASE_URL}${project.videoUrl}`}
                            title={project.name}
                          />
                        ) : project.imageUrls && project.imageUrls.length > 0 ? (
                          <ImageBanner
                            imageUrls={project.imageUrls}
                            alt={project.name}
                            onImageClick={(_, index) =>
                              setModalMedia({
                                images: project.imageUrls!,
                                currentIndex: index,
                                title: project.name,
                              })
                            }
                          />
                        ) : null}
                      </div>
                    </div>
                  )}

                  {/* Description section */}
                  <div className="flex flex-1 min-w-0 flex-col justify-between p-6">
                    <div className="min-w-0">
                      <CardHeader className="p-0 pb-3">
                        <CardTitle className="text-2xl break-words">{project.name}</CardTitle>
                      </CardHeader>

                      <CardContent className="p-0 min-w-0">
                        <CardDescription className="text-sm leading-relaxed text-slate-600 dark:text-slate-300 break-words">
                          {project.description}
                        </CardDescription>

                        {project.installCommands && project.installCommands.length > 0 && (
                          <div className="mt-5 space-y-3">
                            <p className="text-xs font-semibold tracking-wider text-slate-900 uppercase dark:text-slate-200">
                              Install
                            </p>
                            {project.installCommands.map((install) => (
                              <div key={install.label} className="min-w-0">
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
                      </CardContent>
                    </div>

                    <div className="mt-6 flex flex-wrap gap-4 pt-2 text-sm font-semibold">
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
                  </div>
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

      {/* Modal window with image */}
      {modalMedia && (
        <div
          className="fixed inset-0 z-50 flex items-center justify-center bg-black/85 p-4 backdrop-blur-md animate-in fade-in"
          onClick={() => setModalMedia(null)}
        >
          <button
            type="button"
            onClick={() => setModalMedia(null)}
            className="absolute top-5 right-5 z-10 rounded-full bg-white/10 p-2.5 text-white transition hover:bg-white/20 focus:outline-hidden"
            aria-label="Close"
          >
            <X className="h-6 w-6" />
          </button>

          {/* Arrow left */}
          {modalMedia.images.length > 1 && (
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation()
                handlePrevImage()
              }}
              className="absolute left-4 z-10 rounded-full bg-white/10 p-3 text-white transition hover:bg-white/20 focus:outline-hidden"
              aria-label="Previous image"
            >
              <ChevronLeft className="h-6 w-6" />
            </button>
          )}

          {/* Arrow right */}
          {modalMedia.images.length > 1 && (
            <button
              type="button"
              onClick={(e) => {
                e.stopPropagation()
                handleNextImage()
              }}
              className="absolute right-4 z-10 rounded-full bg-white/10 p-3 text-white transition hover:bg-white/20 focus:outline-hidden"
              aria-label="Next image"
            >
              <ChevronRight className="h-6 w-6" />
            </button>
          )}

          <div
            className="relative flex max-h-[90vh] max-w-[92vw] flex-col items-center justify-center"
            onClick={(e) => e.stopPropagation()}
          >
            <img
              src={modalMedia.images[modalMedia.currentIndex]}
              alt={`${modalMedia.title} - ${modalMedia.currentIndex + 1}`}
              className="max-h-[85vh] max-w-[92vw] rounded-lg object-contain shadow-2xl"
            />
            <p className="mt-3 text-center text-sm font-medium text-slate-300">
              {modalMedia.title}
              {modalMedia.images.length > 1 && (
                <span className="ml-2 text-xs text-slate-400">
                  ({modalMedia.currentIndex + 1} / {modalMedia.images.length})
                </span>
              )}
            </p>
          </div>
        </div>
      )}
    </div>
  )
}