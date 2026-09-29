import { List } from "lucide-react"
import type { ReactNode } from "react"
import { useLocation } from "react-router-dom"
import { Link } from "@/components/ui/SiteLink"
import { tutorialChapters } from "@/data/tutorial"
import { docTopicIds } from "@/data/docs-ids"
import { cn } from "@/lib/utils"
import { pagePath } from "@/lib/site-paths"

export interface PageHeading {
  id: string
  title: string
  level?: number
}

interface DocumentationShellProps {
  children: ReactNode
  headings: PageHeading[]
  footer?: ReactNode
}

const topics = docTopicIds.filter((topic) => topic.id !== "quick-start")

function NavigationLink({ to, children }: { to: string; children: ReactNode }) {
  const { pathname } = useLocation()
  const active = pagePath(pathname) === pagePath(to)

  return (
    <Link
      to={to}
      aria-current={active ? "page" : undefined}
      className={cn(
        "block rounded-lg border px-3 py-2 text-sm leading-5 transition-colors",
        active
          ? "border-blue-600 bg-blue-50 font-semibold text-blue-700 dark:bg-blue-950/40 dark:text-blue-300"
          : "border-transparent text-slate-600 hover:bg-slate-100 hover:text-slate-950 dark:text-slate-400 dark:hover:bg-slate-900 dark:hover:text-white"
      )}
    >
      {children}
    </Link>
  )
}

export function DocumentationShell({ children, headings, footer }: DocumentationShellProps) {
  const { pathname } = useLocation()
  const isBlog = /(?:^|\/)blog(?:\/|$)/.test(pathname)

  return (
    <div className="min-h-screen bg-linear-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="mx-auto grid max-w-[1600px] grid-cols-1 gap-10 px-5 py-10 lg:grid-cols-[260px_minmax(0,1fr)] lg:px-8 xl:grid-cols-[260px_minmax(0,1fr)_220px]">
        <aside className="lg:sticky lg:top-24 lg:max-h-[calc(100vh-7rem)] lg:self-start lg:overflow-y-auto">
          {isBlog ? (
            <>
              <div className="mb-3 text-xs font-bold tracking-[0.18em] text-slate-500 uppercase dark:text-slate-400">
                Blog
              </div>
              <nav aria-label="Blog articles" className="flex flex-col gap-1">
                {topics.map((topic) => (
                  <NavigationLink key={topic.id} to={`/blog/${topic.id}`}>
                    {topic.title}
                  </NavigationLink>
                ))}
              </nav>
            </>
          ) : (
            <>
              <div className="mb-3 text-xs font-bold tracking-[0.18em] text-slate-500 uppercase dark:text-slate-400">
                Tutorial
              </div>
              <nav aria-label="Tutorial chapters" className="flex flex-col gap-1">
                {tutorialChapters.map((chapter) => (
                  <NavigationLink key={chapter.slug} to={`/docs/tutorial/${chapter.slug}`}>
                    Chapter {chapter.number} · {chapter.shortTitle}
                  </NavigationLink>
                ))}
              </nav>
            </>
          )}
        </aside>

        <main className="min-w-0">
          {children}
          {footer}
        </main>

        <aside className="hidden xl:block">
          <div className="sticky top-24 rounded-xl border border-slate-200 bg-white/70 p-4 dark:border-slate-800 dark:bg-slate-950/70">
            <div className="mb-3 flex items-center gap-2 text-xs font-bold tracking-wide text-slate-500 uppercase dark:text-slate-400">
              <List className="h-4 w-4" /> On this page
            </div>
            <nav aria-label="Page sections" className="flex flex-col gap-1">
              {headings.map((heading) => (
                <a
                  key={heading.id}
                  href={`#${heading.id}`}
                  className={cn(
                    "rounded py-1.5 text-sm leading-5 text-slate-600 hover:bg-slate-100 hover:text-blue-700 dark:text-slate-400 dark:hover:bg-slate-900 dark:hover:text-blue-300",
                    heading.level === 3 ? "pr-2 pl-5" : "px-2"
                  )}
                >
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
