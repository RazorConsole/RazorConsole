import { useEffect, useMemo, useState } from "react"
import { useParams, useNavigate, useLocation } from "react-router-dom"
import GithubSlugger from "github-slugger"
import { ResponsiveSidebar } from "@/components/ui/ResponsiveSidebar"

import quickStartDoc from "@/docs/quick-start.md?raw"
import builtInComponentsDoc from "@/docs/built-in-components.md?raw"
import hotReloadDoc from "@/docs/hot-reload.md?raw"
import customTranslatorsDoc from "@/docs/custom-translators.md?raw"
import keyboardEventsDoc from "@/docs/keyboard-events.md?raw"
import focusManagementDoc from "@/docs/focus-management.md?raw"
import aotDoc from "@/docs/native-aot-support.md?raw"
import vdomDebuggingDoc from "@/docs/vdom-debugging.md?raw"
import routingDoc from "@/docs/routing.md?raw"
import componentGalleryDoc from "@/docs/component-gallery.md?raw"
import v0_1_1ReleaseNotes from "../../../release-notes/v0.1.1.md?raw"
import v0_2_0ReleaseNotes from "../../../release-notes/v0.2.0.md?raw"
import v0_2_2ReleaseNotes from "../../../release-notes/v0.2.2.md?raw"
import v0_3_0ReleaseNotes from "../../../release-notes/v0.3.0.md?raw"
import type { Heading, TopicItem } from "@/types/docs/topicItem"
import Sidebar from "@/components/docs/Sidebar"
import EditLink from "@/components/docs/EditLink"

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
        .replace(/`([^`]+)`/g, "$1") // `code` -> code
        .replace(/\[([^\]]+)\]\([^\)]+\)/g, "$1") // [link](url) -> link
        .replace(/[*_]{1,2}([^*_]+)[*_]{1,2}/g, "$1") // **bold** -> bold

      const slug = slugger.slug(cleanTitle)

      headings.push({ level, title: rawTitle, id: slug })
    }
  }
  return headings
}

function getFilePathForTopic(
  topicId: string,
  topics: TopicItem[],
  releaseNotes: TopicItem[]
): string {
  const topic = topics.find((t) => t.id === topicId) || releaseNotes.find((r) => r.id === topicId)
  return topic?.filePath || ""
}

export default function Docs() {
  const topics: TopicItem[] = useMemo(() => {
    const rawTopics = [
      {
        id: "quick-start",
        title: "Quick Start",
        content: quickStartDoc,
        filePath: "website/src/docs/quick-start.md",
      },
      {
        id: "built-in-components",
        title: "Built-in Components",
        content: builtInComponentsDoc,
        filePath: "website/src/docs/built-in-components.md",
      },
      {
        id: "hot-reload",
        title: "Hot Reload",
        content: hotReloadDoc,
        filePath: "website/src/docs/hot-reload.md",
      },
      {
        id: "cli-routing",
        title: "Routing",
        content: routingDoc,
        filePath: "website/src/docs/routing.md",
      },
      {
        id: "native-aot",
        title: "Native Ahead-of-Time Compilation",
        content: aotDoc,
        filePath: "website/src/docs/native-aot-support.md",
      },
      {
        id: "custom-translators",
        title: "Custom Translators",
        content: customTranslatorsDoc,
        filePath: "website/src/docs/custom-translators.md",
      },
      {
        id: "keyboard-events",
        title: "Keyboard Events",
        content: keyboardEventsDoc,
        filePath: "website/src/docs/keyboard-events.md",
      },
      {
        id: "focus-management",
        title: "Focus Management",
        content: focusManagementDoc,
        filePath: "website/src/docs/focus-management.md",
      },
      {
        id: "vdom-debugging",
        title: "VDom Tree Debugging",
        content: vdomDebuggingDoc,
        filePath: "website/src/docs/vdom-debugging.md",
      },
      {
        id: "component-gallery",
        title: "Component Gallery",
        content: componentGalleryDoc,
        filePath: "website/src/docs/component-gallery.md",
      },
    ]
    return rawTopics.map((topic) => ({ ...topic, headings: extractHeadings(topic.content) }))
  }, [])

  const releaseNotes: TopicItem[] = useMemo(() => {
    const rawNotes = [
      {
        id: "v0.3.0",
        title: "v0.3.0",
        content: v0_3_0ReleaseNotes,
        filePath: "release-notes/v0.3.0.md",
      },
      {
        id: "v0.2.2",
        title: "v0.2.2",
        content: v0_2_2ReleaseNotes,
        filePath: "release-notes/v0.2.2.md",
      },
      {
        id: "v0.2.0",
        title: "v0.2.0",
        content: v0_2_0ReleaseNotes,
        filePath: "release-notes/v0.2.0.md",
      },
      {
        id: "v0.1.1",
        title: "v0.1.1",
        content: v0_1_1ReleaseNotes,
        filePath: "release-notes/v0.1.1.md",
      },
    ]
    return rawNotes.map((note) => ({ ...note, headings: extractHeadings(note.content) }))
  }, [])

  const { topicId } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const activeId = topicId || topics[0].id
  const [expandedTopics, setExpandedTopics] = useState<Set<string>>(new Set([activeId]))
  const [releaseNotesOpen, setReleaseNotesOpen] = useState(false)

  useEffect(() => {
    if (topics.some((t) => t.id === activeId)) {
      setExpandedTopics((prev) => {
        const newSet = new Set(prev)
        newSet.add(activeId)
        return newSet
      })
    } else if (releaseNotes.some((r) => r.id === activeId)) {
      setReleaseNotesOpen(true)
    }
  }, [activeId, topics, releaseNotes])

  useEffect(() => {
    if (location.hash) {
      const id = location.hash.replace("#", "")
      setTimeout(() => {
        document.getElementById(id)?.scrollIntoView({ behavior: "smooth", block: "start" })
      }, 100)
    }
  }, [location.hash, activeId])

  const activeTopic = useMemo(
    () =>
      topics.find((topic) => topic.id === activeId) ??
      releaseNotes.find((note) => note.id === activeId) ??
      topics[0],
    [activeId, topics, releaseNotes]
  )

  const handleTopicClick = (id: string) => {
    navigate(`/docs/${id}`)
    setExpandedTopics((prev) => {
      const newSet = new Set(prev)
      newSet.add(id)
      return newSet
    })
  }
  const toggleTopicExpand = (e: React.MouseEvent, id: string) => {
    e.stopPropagation()
    setExpandedTopics((prev) => {
      const newSet = new Set(prev)
      if (newSet.has(id)) newSet.delete(id)
      else newSet.add(id)
      return newSet
    })
  }
  const handleSubHeadingClick = (e: React.MouseEvent, topicId: string, headingId: string) => {
    e.stopPropagation()
    navigate(`/docs/${topicId}#${headingId}`)
  }

  return (
    <div className="min-h-screen bg-gradient-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="px-6 py-16 sm:px-10 lg:px-16">
        <div className="flex flex-col lg:block">
          <ResponsiveSidebar breakpoint="lg" className="w-72 px-6 py-6">
            <Sidebar
              topics={topics}
              releaseNotes={releaseNotes}
              activeTopic={activeTopic}
              expandedTopics={expandedTopics}
              releaseNotesOpen={releaseNotesOpen}
              handleTopicClick={handleTopicClick}
              handleSubHeadingClick={handleSubHeadingClick}
              toggleTopicExpand={toggleTopicExpand}
              setReleaseNotesOpen={setReleaseNotesOpen}
            />
          </ResponsiveSidebar>

          <main className="min-w-0 flex-1">
            <EditLink
              activeTopic={activeTopic}
              topics={topics}
              releaseNotes={releaseNotes}
              getFilePathForTopic={getFilePathForTopic}
            />
          </main>
        </div>
      </div>
    </div>
  )
}
