import { ChevronLeft, ChevronRight } from "lucide-react"
import { Link } from "react-router-dom"
import { DocumentationShell } from "@/components/docs/DocumentationShell"
import { TutorialChapterContent } from "./TutorialChapterContent"
import { tutorialChapters, type TutorialChapter } from "@/data/tutorial"

function slugify(value: string) {
  return value
    .toLowerCase()
    .replace(/[`*_]/g, "")
    .replace(/[^\p{L}\p{N}\s-]/gu, "")
    .trim()
    .replace(/\s+/g, "-")
}

function getHeadings(content: string) {
  return [...content.matchAll(/^##\s+(.+)$/gm)].map((match) => ({
    title: match[1].replace(/[`*_]/g, ""),
    id: slugify(match[1]),
    level: 2,
  }))
}

export function TutorialLayout({ chapter }: { chapter: TutorialChapter }) {
  const headings = getHeadings(chapter.content)
  const previous = tutorialChapters[chapter.number - 2]
  const next = tutorialChapters[chapter.number]

  return (
    <DocumentationShell
      headings={headings}
      footer={
          <nav aria-label="Adjacent tutorial chapters" className="mt-12 flex items-stretch justify-between gap-4 border-t border-slate-200 pt-6 dark:border-slate-800">
            {previous ? (
              <Link to={`/docs/tutorial/${previous.slug}`} className="flex items-center gap-2 rounded-lg px-3 py-2 text-sm text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-900">
                <ChevronLeft className="h-4 w-4" /> Chapter {previous.number}
              </Link>
            ) : <span />}
            {next ? (
              <Link to={`/docs/tutorial/${next.slug}`} className="flex items-center gap-2 rounded-lg px-3 py-2 text-right text-sm text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-slate-900">
                Chapter {next.number} <ChevronRight className="h-4 w-4" />
              </Link>
            ) : <span />}
          </nav>
      }
    >
          <article className="prose prose-slate max-w-none dark:prose-invert">
            <TutorialChapterContent chapter={chapter} />
          </article>
    </DocumentationShell>
  )
}
