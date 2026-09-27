import { RotateCcw, Terminal } from "lucide-react"
import { useId, useState } from "react"
import XTermPreview from "@/components/components/XTermPreview"
import { Button } from "@/components/ui/Button"
import { MarkdownRenderer } from "@/components/ui/Markdown"
import type { TutorialChapter } from "@/data/tutorial"

const PREVIEW_MARKER = "<!-- interactive-preview -->"

export function TutorialChapterContent({ chapter }: { chapter: TutorialChapter }) {
  const [generation, setGeneration] = useState(0)
  const reactId = useId().replace(/[^a-zA-Z0-9_-]/g, "")
  const instanceId = `tutorial-${chapter.slug}-${reactId}-${generation}`
  const [introduction, lesson = ""] = chapter.content.split(PREVIEW_MARKER)

  return (
    <div>
      <MarkdownRenderer content={introduction} />

      <section
        aria-labelledby="try-it-first-title"
        className="not-prose my-8 overflow-hidden rounded-2xl border border-violet-200 bg-white shadow-sm dark:border-violet-900/70 dark:bg-slate-950"
      >
        <div className="flex flex-col gap-4 border-b border-slate-200 px-5 py-5 sm:flex-row sm:items-start sm:justify-between dark:border-slate-800">
          <div>
            <div className="mb-2 flex items-center gap-2 text-sm font-semibold text-violet-700 dark:text-violet-300">
              <Terminal className="h-4 w-4" aria-hidden="true" />
              Try it first
            </div>
            <h2 id="try-it-first-title" className="text-xl font-bold text-slate-950 dark:text-white">
              Run the real component in your browser
            </h2>
            <p className="mt-2 max-w-2xl text-sm leading-6 text-slate-600 dark:text-slate-300">
              {chapter.previewInstructions} Restart creates a fresh, isolated component.
            </p>
          </div>
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="shrink-0 gap-2"
            onClick={() => setGeneration((value) => value + 1)}
          >
            <RotateCcw className="h-4 w-4" aria-hidden="true" />
            Restart preview
          </Button>
        </div>

        <div className="p-3 sm:p-5">
          <XTermPreview
            key={instanceId}
            elementId={instanceId}
            componentId={chapter.componentId}
            className={chapter.previewHeight ?? "h-[260px]"}
          />
        </div>
      </section>

      <MarkdownRenderer content={lesson} />
    </div>
  )
}
