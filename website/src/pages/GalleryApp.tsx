/* eslint-disable react-refresh/only-export-components */
import ImageBanner from "@/components/ui/ImageBanner"
import VideoBanner from "@/components/ui/VideoBanner"
import { CopyButton } from "@/components/ui/CopyButton"
import { Link } from "@/components/ui/SiteLink"
import { getOfficialApp } from "@/data/official-apps"
import { getPageUrl } from "@/lib/utils"
import { ArrowLeft, Download, ExternalLink, Github, PackageOpen, Terminal } from "lucide-react"
import type { MetaFunction } from "react-router"
import { useParams } from "react-router"

export const meta: MetaFunction = ({ matches, location, params }) => {
  const rootMeta = matches.find((match) => match.id === "root")?.meta || []
  const app = getOfficialApp(params.appSlug)
  const title = app ? `${app.name} | RazorConsole Gallery` : "App not found | RazorConsole"
  const description = app?.description ?? "The requested RazorConsole app could not be found."
  const url = getPageUrl(location.pathname)

  return [
    ...rootMeta,
    { title },
    { name: "description", content: description },
    { property: "og:type", content: "website" },
    { property: "og:title", content: title },
    { property: "og:description", content: description },
    { property: "og:url", content: url },
    { name: "twitter:card", content: "summary_large_image" },
    { name: "twitter:title", content: title },
    { name: "twitter:description", content: description },
  ]
}

export default function GalleryApp() {
  const { appSlug } = useParams()
  const app = getOfficialApp(appSlug)

  if (!app) {
    return (
      <main className="container mx-auto min-h-[60vh] px-4 py-20 text-center">
        <PackageOpen className="mx-auto h-14 w-14 text-slate-400" aria-hidden="true" />
        <h1 className="mt-6 text-3xl font-bold text-slate-950 dark:text-white">App not found</h1>
        <Link
          className="mt-6 inline-block font-medium text-violet-700 hover:underline dark:text-violet-300"
          to="/gallery"
        >
          Return to the gallery
        </Link>
      </main>
    )
  }

  const pageUrl = getPageUrl(`/gallery/${app.slug}`)
  const structuredData = {
    "@context": "https://schema.org",
    "@type": "SoftwareApplication",
    name: app.name,
    description: app.description,
    url: pageUrl,
    applicationCategory: app.name.includes("Gallery") ? "DeveloperApplication" : "GameApplication",
    operatingSystem: "Windows, macOS, Linux",
    softwareRequirements: "Native terminal with no .NET runtime required",
    codeRepository: app.repositoryUrl,
    downloadUrl: app.downloadUrl,
    isAccessibleForFree: true,
  }

  return (
    <main className="min-h-screen bg-linear-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <script type="application/ld+json">{JSON.stringify(structuredData)}</script>
      <div className="container mx-auto max-w-6xl px-4 py-12 sm:py-16">
        <Link
          className="inline-flex items-center gap-2 text-sm font-medium text-slate-600 hover:text-violet-700 dark:text-slate-400 dark:hover:text-violet-300"
          to="/gallery"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden="true" />
          All official apps
        </Link>

        <div className="mt-8 grid gap-10 lg:grid-cols-[minmax(0,1.35fr)_minmax(300px,0.65fr)]">
          <div>
            <div className="overflow-hidden rounded-2xl border border-slate-200 bg-slate-950 shadow-xl dark:border-slate-800">
              {app.videoUrl ? (
                <VideoBanner src={`${import.meta.env.BASE_URL}${app.videoUrl}`} title={app.name} />
              ) : app.imageUrls && app.imageUrls.length > 0 ? (
                <ImageBanner imageUrls={app.imageUrls} alt={app.name} />
              ) : (
                <div className="flex h-80 items-center justify-center bg-linear-to-br from-slate-950 via-slate-900 to-violet-950">
                  <img
                    src={`${import.meta.env.BASE_URL}razorconsole-icon.svg`}
                    alt=""
                    className="h-28 w-28 drop-shadow-2xl"
                  />
                </div>
              )}
            </div>

            <section className="mt-10">
              <div className="flex flex-wrap items-center gap-3">
                <span className="rounded-full bg-violet-100 px-3 py-1 text-xs font-semibold tracking-wide text-violet-800 uppercase dark:bg-violet-500/15 dark:text-violet-300">
                  Official app
                </span>
                <span className="rounded-full bg-emerald-100 px-3 py-1 text-xs font-semibold tracking-wide text-emerald-800 uppercase dark:bg-emerald-500/15 dark:text-emerald-300">
                  Native AOT
                </span>
              </div>
              <h1 className="mt-4 text-4xl font-bold tracking-tight text-slate-950 sm:text-5xl dark:text-white">
                {app.name}
              </h1>
              <p className="mt-5 max-w-3xl text-lg leading-8 text-slate-600 dark:text-slate-300">
                {app.description}
              </p>
              <div className="mt-7 flex flex-wrap gap-3">
                <a
                  className="inline-flex h-10 items-center gap-2 rounded-md border border-slate-200 bg-white px-4 text-sm font-medium text-slate-900 transition-colors hover:bg-slate-100 dark:border-slate-700 dark:bg-slate-900 dark:text-white dark:hover:bg-slate-800"
                  href={app.sourceUrl}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  <Github className="h-4 w-4" aria-hidden="true" />
                  View source
                </a>
                {app.downloadUrl && (
                  <a
                    className="inline-flex h-10 items-center gap-2 rounded-md border border-slate-200 bg-white px-4 text-sm font-medium text-slate-900 transition-colors hover:bg-slate-100 dark:border-slate-700 dark:bg-slate-900 dark:text-white dark:hover:bg-slate-800"
                    href={app.downloadUrl}
                    target="_blank"
                    rel="noopener noreferrer"
                  >
                    <Download className="h-4 w-4" aria-hidden="true" />
                    Download binaries
                  </a>
                )}
              </div>
            </section>
          </div>

          <aside>
            <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-950">
              <div className="flex items-center gap-3">
                <span className="flex h-10 w-10 items-center justify-center rounded-xl bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300">
                  <Terminal className="h-5 w-5" aria-hidden="true" />
                </span>
                <div>
                  <h2 className="text-xl font-semibold text-slate-950 dark:text-white">Install</h2>
                  <p className="text-sm text-slate-500 dark:text-slate-400">Stable release</p>
                </div>
              </div>

              <div className="mt-6 space-y-5">
                {app.installCommands?.map((install) => (
                  <div key={install.label}>
                    <div className="mb-2 flex items-center justify-between gap-3">
                      <h3 className="text-sm font-semibold text-slate-800 dark:text-slate-200">
                        {install.label}
                      </h3>
                      <CopyButton content={install.command} />
                    </div>
                    <pre className="overflow-x-auto rounded-xl border border-slate-800 bg-slate-950 p-4 text-xs leading-5 text-slate-200">
                      <code>{install.command}</code>
                    </pre>
                  </div>
                ))}
              </div>

              <p className="mt-5 text-xs leading-5 text-slate-500 dark:text-slate-400">
                The installer detects your operating system and CPU architecture, downloads the
                signed release archive, and verifies its SHA-256 checksum.
              </p>
            </section>

            <section className="mt-6 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-950">
              <h2 className="text-lg font-semibold text-slate-950 dark:text-white">App details</h2>
              <dl className="mt-4 space-y-4 text-sm">
                <div>
                  <dt className="text-slate-500 dark:text-slate-400">Package</dt>
                  <dd className="mt-1 font-mono text-slate-900 dark:text-slate-100">
                    {app.packageId}
                  </dd>
                </div>
                <div>
                  <dt className="text-slate-500 dark:text-slate-400">Command</dt>
                  <dd className="mt-1 font-mono text-slate-900 dark:text-slate-100">
                    {app.commandName}
                  </dd>
                </div>
              </dl>
              {app.website && app.website !== app.sourceUrl && (
                <a
                  className="mt-5 inline-flex items-center gap-1.5 text-sm font-medium text-violet-700 hover:underline dark:text-violet-300"
                  href={app.website}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  Project documentation
                  <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
                </a>
              )}
            </section>
          </aside>
        </div>
      </div>
    </main>
  )
}
