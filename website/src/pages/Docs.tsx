import { useEffect, useState } from "react"
import { useParams, useNavigate, useLocation, useLoaderData, type LoaderFunctionArgs } from "react-router"
import GithubSlugger from "github-slugger"
import { ResponsiveSidebar } from "@/components/ui/ResponsiveSidebar"
import type { Heading } from "@/types/docs/topicItem"
import Sidebar from "@/components/docs/Sidebar"
import EditLink from "@/components/docs/EditLink"
import { docTopicIds, releaseNoteIds } from "@/data/docs-ids"
import type { MetaFunction } from "react-router"
import { MarkdownRenderer } from "@/components/ui/Markdown"
import { stripMarkdown } from "@/lib/utils"

const docsModules = import.meta.glob("/src/docs/*.md", { query: "?raw", import: "default" })
const releaseModules = import.meta.glob("/../release-notes/*.md", { query: "?raw", import: "default" })

export const meta: MetaFunction<typeof loader> = ({ data }) => {
  const topic = data;
  const title = topic ? `${topic.title} | RazorConsole Docs` : "Documentation | RazorConsole";
  const description = topic ? 
    stripMarkdown(topic.content).slice(0, 150) + "..." : 
    "Explore RazorConsole documentation to learn how to build powerful terminal user interfaces.";

  return [
    { title },
    { name: "description", content: description },
    { property: "og:title", content: title },
    { property: "og:description", content: description },
  ];
};

function extractHeadings(markdown: string): Heading[] {
  const lines = markdown.split(/\r?\n/)
  const headings: Heading[] = []
  const slugger = new GithubSlugger()
  let inCodeBlock = false

  for (const line of lines) {
    if (line.trim().startsWith("```")) {
      inCodeBlock = !inCodeBlock
      continue
    }
    if (inCodeBlock) continue

    const match = line.match(/^\s*(#{1,4})\s+(.+)$/)
    if (match) {
      const level = match[1].length
      const rawTitle = match[2].trim()
      const cleanTitle = rawTitle
        .replace(/`([^`]+)`/g, "$1")
        .replace(/\[([^\]]+)\]\([^\)]+\)/g, "$1")
        .replace(/[*_]{1,2}([^*_]+)[*_]{1,2}/g, "$1")

      headings.push({ level, title: rawTitle, id: slugger.slug(cleanTitle) })
    }
  }
  return headings
}

type Topic = { id: string; title: string; content: string; filePath: string; headings: Heading[]; }
async function loadMarkdownContent(topicId: string) {
  const topicMeta = docTopicIds.find(t => t.id === topicId)
  const releaseMeta = releaseNoteIds.find(r => r.id === topicId)
  const meta = topicMeta || releaseMeta || docTopicIds[0]

  const modules = !!releaseMeta ? releaseModules : docsModules
  const fileName = meta.filePath.split('/').pop()?.toLowerCase()

  const loadFileKey = Object.keys(modules).find(k => k.toLowerCase().endsWith(`/${fileName}`))
  const loadFile = loadFileKey ? modules[loadFileKey] : null

  if (!loadFile) {
    throw new Error(`Markdown file not found for: ${topicId} (looked for ${fileName})`)
  }

  const rawContent = (await loadFile()) as string
  return {
    ...meta,
    content: rawContent,
    headings: extractHeadings(rawContent)
  }
}

export async function loader({ params }: LoaderFunctionArgs) {
  return await loadMarkdownContent(params.topicId || "quick-start")
}

export async function clientLoader({ params }: LoaderFunctionArgs) {
  return await loadMarkdownContent(params.topicId || "quick-start")
}

clientLoader.hydrate = true;

export default function Docs() {
  const activeTopic = useLoaderData<Topic>();

  const { topicId } = useParams()
  const navigate = useNavigate()
  const location = useLocation()

  const activeId = topicId || docTopicIds[0].id
  const [expandedTopics, setExpandedTopics] = useState<Set<string>>(new Set([activeId]))
  const [releaseNotesOpen, setReleaseNotesOpen] = useState(activeId.startsWith("v0."))

  const topicsWithHeadings = docTopicIds.map(t => ({
    ...t,
    headings: t.id === activeTopic.id ? activeTopic.headings : []
  }))

  const releaseWithHeadings = releaseNoteIds.map(r => ({
    ...r,
    headings: r.id === activeTopic.id ? activeTopic.headings : []
  }))

  useEffect(() => {
    if (docTopicIds.some((t) => t.id === activeId)) {
      setExpandedTopics((prev) => new Set(prev).add(activeId))
    } else if (releaseNoteIds.some((r) => r.id === activeId)) {
      setReleaseNotesOpen(true)
    }
  }, [activeId])

  useEffect(() => {
    if (location.hash) {
      const id = location.hash.replace("#", "")
      const element = document.getElementById(id)
      if (element) {
        setTimeout(() => element.scrollIntoView({ behavior: "smooth", block: "start" }), 100)
      }
    }
  }, [location.hash, activeId])

  return (
    <div className="min-h-screen docs bg-linear-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="px-6 py-16 sm:px-10 lg:px-16">
        <div className="flex flex-col lg:block">
          <ResponsiveSidebar breakpoint="lg" className="w-72 px-6 py-6">
            <Sidebar
              topics={topicsWithHeadings as any}
              releaseNotes={releaseWithHeadings as any}
              activeTopic={activeTopic as any}
              expandedTopics={expandedTopics}
              releaseNotesOpen={releaseNotesOpen}
              handleTopicClick={(id) => navigate(`/docs/${id}`)}
              handleSubHeadingClick={(_, tId, hId) => navigate(`/docs/${tId}#${hId}`)}
              toggleTopicExpand={(_, id) => setExpandedTopics(p => {
                const n = new Set(p); n.has(id) ? n.delete(id) : n.add(id); return n;
              })}
              setReleaseNotesOpen={setReleaseNotesOpen}
            />
          </ResponsiveSidebar>

          <main className="min-w-0 flex-1">
            <div className="prose prose-slate dark:prose-invert max-w-none">
              <MarkdownRenderer content={activeTopic.content} />
            </div>

            <EditLink
              activeTopic={activeTopic as any}
              topics={topicsWithHeadings as any}
              releaseNotes={releaseWithHeadings as any}
              getFilePathForTopic={() => activeTopic.filePath}
            />
          </main>
        </div>
      </div>
    </div>
  )
}