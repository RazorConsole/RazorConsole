import { useEffect } from "react"
import { useLocation, useLoaderData, redirect, type LoaderFunctionArgs } from "react-router"
import GithubSlugger from "github-slugger"
import type { Heading } from "@/types/docs/topicItem"
import EditLink from "@/components/docs/EditLink"
import { DocumentationShell } from "@/components/docs/DocumentationShell"
import { docTopicIds, releaseNoteIds } from "@/data/docs-ids"
import type { MetaFunction } from "react-router"
import { MarkdownRenderer } from "@/components/ui/Markdown"
import { getFullSitePath, stripMarkdown } from "@/lib/utils"

const docsModules = import.meta.glob("/src/docs/*.md", { query: "?raw", import: "default" })

export const meta: MetaFunction<typeof loader> = ({ data, matches, location }) => {
  const rootMeta = matches.find((m) => m.id === "root")?.meta || [];
  const pageUrl = `${getFullSitePath()}${location.pathname}`;

  const topic = data;
  const title = topic ? `${topic.title} | RazorConsole Docs` : "Documentation | RazorConsole";
  const description = topic ?
    stripMarkdown(topic.content).slice(0, 150) + "..." :
    "Explore RazorConsole documentation to learn how to build powerful terminal user interfaces.";

  return [
    ...rootMeta,
    { title },
    { property: "og:url", content: pageUrl },
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
        .replace(/`([^`]+)`/g, "$1") // Unwrap `code` to code
        .replace(/\[([^\]]+)\]\([^\)]+\)/g, "$1") // Unwrap [link](url) to link
        .replace(/[*_]{1,2}([^*_]+)[*_]{1,2}/g, "$1") // Unwrap **bold** to bold

      headings.push({ level, title: rawTitle, id: slugger.slug(cleanTitle) })
    }
  }
  return headings
}

type Topic = { id: string; title: string; content: string; filePath: string; headings: Heading[]; }
async function loadMarkdownContent(topicId: string) {
  const topicMeta = docTopicIds.find(t => t.id === topicId)
  const meta = topicMeta || docTopicIds[0]

  const modules = docsModules
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
  if (!params.topicId || params.topicId === "quick-start") return redirect("/docs/tutorial/hello-world")
  if (releaseNoteIds.some((note) => note.id === params.topicId)) return redirect(`/release-notes/${params.topicId}`)
  return await loadMarkdownContent(params.topicId || "quick-start")
}

export async function clientLoader({ params }: LoaderFunctionArgs) {
  if (!params.topicId || params.topicId === "quick-start") return redirect("/docs/tutorial/hello-world")
  if (releaseNoteIds.some((note) => note.id === params.topicId)) return redirect(`/release-notes/${params.topicId}`)
  return await loadMarkdownContent(params.topicId || "quick-start")
}

clientLoader.hydrate = true;

export default function Docs() {
  const activeTopic = useLoaderData<Topic>();
  const location = useLocation()

  const topicsWithHeadings = docTopicIds.map(t => ({
    ...t,
    headings: t.id === activeTopic.id ? activeTopic.headings : []
  }))

  useEffect(() => {
    if (location.hash) {
      const id = location.hash.replace("#", "")
      const element = document.getElementById(id)
      if (element) {
        setTimeout(() => element.scrollIntoView({ behavior: "smooth", block: "start" }), 100)
      }
    }
  }, [location.hash, activeTopic.id])

  return (
    <DocumentationShell headings={activeTopic.headings.filter((heading) => heading.level === 2 || heading.level === 3)}>
      <article className="prose prose-slate max-w-none dark:prose-invert">
        <MarkdownRenderer content={activeTopic.content} />
      </article>
      <EditLink
        activeTopic={activeTopic as any}
        topics={topicsWithHeadings as any}
        releaseNotes={[]}
        getFilePathForTopic={() => activeTopic.filePath}
      />
    </DocumentationShell>
  )
}
