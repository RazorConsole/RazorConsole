import type { TopicItem } from "@/types/docs/topicItem"
import { MarkdownRenderer } from "@/components/ui/Markdown"

interface Props {
  activeTopic: TopicItem | null
  topics: TopicItem[]
  releaseNotes: TopicItem[]
  getFilePathForTopic(topicId: string, topics: TopicItem[], releaseNotes: TopicItem[]): string
}

const EditLink: React.FC<Props> = ({ activeTopic, topics, releaseNotes, getFilePathForTopic }) => (
  <>
    {activeTopic && (
      <article key={activeTopic.id} className="prose prose-slate dark:prose-invert max-w-none">
        <MarkdownRenderer content={activeTopic.content} />
        {(() => {
          const filePath = getFilePathForTopic(activeTopic.id, topics, releaseNotes)
          return (
            filePath != null && (
              <div className="not-prose mt-8 border-t border-slate-200 pt-6 dark:border-slate-700">
                <a
                  href={`https://github.com/RazorConsole/RazorConsole/edit/main/${filePath}`}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="flex items-center gap-2 text-sm text-slate-600 transition-colors hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-100"
                >
                  Edit on GitHub
                </a>
              </div>
            )
          )
        })()}
      </article>
    )}
  </>
)

export default EditLink
