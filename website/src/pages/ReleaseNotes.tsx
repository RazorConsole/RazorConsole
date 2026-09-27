import { List } from "lucide-react"
import { useEffect } from "react"
import { Link, redirect, useLoaderData, useLocation, type LoaderFunctionArgs, type MetaFunction } from "react-router"
import GithubSlugger from "github-slugger"
import { MarkdownRenderer } from "@/components/ui/Markdown"
import { releaseNoteIds } from "@/data/docs-ids"
import { cn, getFullSitePath, stripMarkdown } from "@/lib/utils"

const releaseModules = import.meta.glob("/../release-notes/*.md", { query: "?raw", import: "default" })

type ReleaseNote = {
  id: string
  title: string
  filePath: string
  content: string
  headings: Array<{ id: string; title: string; level: number }>
}

function extractHeadings(markdown: string) {
  const slugger = new GithubSlugger()
  let inCodeBlock = false
  return markdown.split(/\r?\n/).flatMap((line) => {
    if (line.trim().startsWith("```")) { inCodeBlock = !inCodeBlock; return [] }
    if (inCodeBlock) return []
    const match = line.match(/^\s*(#{2,3})\s+(.+)$/)
    if (!match) return []
    const title = match[2].replace(/`([^`]+)`/g, "$1").replace(/[*_]/g, "")
    return [{ id: slugger.slug(title), title, level: match[1].length }]
  })
}

async function loadRelease(version: string) {
  const meta = releaseNoteIds.find((note) => note.id === version) ?? releaseNoteIds[0]
  const fileName = meta.filePath.split("/").at(-1)?.toLowerCase()
  const key = Object.keys(releaseModules).find((path) => path.toLowerCase().endsWith(`/${fileName}`))
  if (!key) throw new Error(`Release note not found: ${version}`)
  const content = await releaseModules[key]() as string
  return { ...meta, content, headings: extractHeadings(content) }
}

export async function loader({ params }: LoaderFunctionArgs) {
  if (!params.version) return redirect(`/release-notes/${releaseNoteIds[0].id}`)
  return loadRelease(params.version)
}

export async function clientLoader({ params }: LoaderFunctionArgs) {
  if (!params.version) return redirect(`/release-notes/${releaseNoteIds[0].id}`)
  return loadRelease(params.version)
}

clientLoader.hydrate = true

export const meta: MetaFunction<typeof loader> = ({ data, location }) => {
  const note = data as ReleaseNote | undefined
  const title = note ? `${note.title} | RazorConsole Release Notes` : "Release Notes | RazorConsole"
  const description = note ? `${stripMarkdown(note.content).slice(0, 150)}...` : "RazorConsole release notes."
  return [
    { title },
    { name: "description", content: description },
    { property: "og:title", content: title },
    { property: "og:description", content: description },
    { property: "og:url", content: `${getFullSitePath()}${location.pathname}` },
  ]
}

export default function ReleaseNotes() {
  const note = useLoaderData<ReleaseNote>()
  const location = useLocation()

  useEffect(() => {
    if (!location.hash) return
    const element = document.getElementById(location.hash.slice(1))
    if (element) setTimeout(() => element.scrollIntoView({ behavior: "smooth", block: "start" }), 100)
  }, [location.hash, note.id])

  return (
    <div className="min-h-screen bg-linear-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="mx-auto grid max-w-[1500px] grid-cols-1 gap-10 px-5 py-10 lg:grid-cols-[220px_minmax(0,1fr)] lg:px-8 xl:grid-cols-[220px_minmax(0,1fr)_220px]">
        <aside className="lg:sticky lg:top-24 lg:self-start">
          <div className="mb-3 text-xs font-bold tracking-[0.18em] text-slate-500 uppercase dark:text-slate-400">Releases</div>
          <nav aria-label="Release versions" className="flex flex-col gap-1">
            {releaseNoteIds.map((release) => (
              <Link
                key={release.id}
                to={`/release-notes/${release.id}`}
                className={cn(
                  "rounded-lg border px-3 py-2 text-sm",
                  release.id === note.id
                    ? "border-blue-600 bg-blue-50 font-semibold text-blue-700 dark:bg-blue-950/40 dark:text-blue-300"
                    : "border-transparent text-slate-600 hover:bg-slate-100 dark:text-slate-400 dark:hover:bg-slate-900",
                )}
              >
                {release.title}
              </Link>
            ))}
          </nav>
        </aside>

        <main className="min-w-0">
          <article className="prose prose-slate max-w-none dark:prose-invert">
            <MarkdownRenderer content={note.content} />
          </article>
        </main>

        <aside className="hidden xl:block">
          <div className="sticky top-24 rounded-xl border border-slate-200 bg-white/70 p-4 dark:border-slate-800 dark:bg-slate-950/70">
            <div className="mb-3 flex items-center gap-2 text-xs font-bold tracking-wide text-slate-500 uppercase dark:text-slate-400">
              <List className="h-4 w-4" /> On this page
            </div>
            <nav className="flex flex-col gap-1">
              {note.headings.map((heading) => (
                <a key={heading.id} href={`#${heading.id}`} className={cn("rounded py-1.5 text-sm text-slate-600 hover:bg-slate-100 dark:text-slate-400 dark:hover:bg-slate-900", heading.level === 3 ? "pl-5 pr-2" : "px-2")}>
                  {heading.title}
                </a>
              ))}
            </nav>
          </div>
        </aside>
      </div>
    </div>
  )
}
