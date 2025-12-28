import { useEffect, useMemo, useState } from "react";
import { useParams, useNavigate, useLocation } from "react-router-dom";
import { cn } from "@/lib/utils";
import { ChevronDown, ChevronRight } from "lucide-react";
import GithubSlugger from "github-slugger";

import quickStartDoc from "@/docs/quick-start.md?raw";
import builtInComponentsDoc from "@/docs/built-in-components.md?raw";
import hotReloadDoc from "@/docs/hot-reload.md?raw";
import customTranslatorsDoc from "@/docs/custom-translators.md?raw";
import keyboardEventsDoc from "@/docs/keyboard-events.md?raw";
import focusManagementDoc from "@/docs/focus-management.md?raw";
import aotDoc from "@/docs/native-aot-support.md?raw";
import vdomDebuggingDoc from "@/docs/vdom-debugging.md?raw";
import routingDoc from "@/docs/routing.md?raw";
import componentGalleryDoc from "@/docs/component-gallery.md?raw";
import v0_1_1ReleaseNotes from "../../../release-notes/v0.1.1.md?raw";
import v0_2_0ReleaseNotes from "../../../release-notes/v0.2.0.md?raw";
import v0_2_2ReleaseNotes from "../../../release-notes/v0.2.2.md?raw";
import v0_3_0ReleaseNotes from "../../../release-notes/v0.3.0.md?raw";
import { MarkdownRenderer } from "@/components/Markdown";


interface Heading {
    level: number;
    title: string;
    id: string;
}

interface TopicItem {
    id: string;
    title: string;
    content: string;
    filePath: string;
    headings: Heading[];
}


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
    const topics: TopicItem[] = useMemo(
        () => {
            const rawTopics = [
                { id: "quick-start", title: "Quick Start", content: quickStartDoc, filePath: "website/src/docs/quick-start.md" },
                { id: "built-in-components", title: "Built-in Components", content: builtInComponentsDoc, filePath: "website/src/docs/built-in-components.md" },
                { id: "hot-reload", title: "Hot Reload", content: hotReloadDoc, filePath: "website/src/docs/hot-reload.md" },
                { id: "cli-routing", title: "Routing", content: routingDoc, filePath: "website/src/docs/routing.md" },
                { id: "native-aot", title: "Native Ahead-of-Time Compilation", content: aotDoc, filePath: "website/src/docs/native-aot-support.md" },
                { id: "custom-translators", title: "Custom Translators", content: customTranslatorsDoc, filePath: "website/src/docs/custom-translators.md" },
                { id: "keyboard-events", title: "Keyboard Events", content: keyboardEventsDoc, filePath: "website/src/docs/keyboard-events.md" },
                { id: "focus-management", title: "Focus Management", content: focusManagementDoc, filePath: "website/src/docs/focus-management.md" },
                { id: "vdom-debugging", title: "VDom Tree Debugging", content: vdomDebuggingDoc, filePath: "website/src/docs/vdom-debugging.md" },
                { id: "component-gallery", title: "Component Gallery", content: componentGalleryDoc, filePath: "website/src/docs/component-gallery.md" },
            ];

            return rawTopics.map(topic => ({
                ...topic,
                headings: extractHeadings(topic.content)
            }));
        },
        []
    );

    const releaseNotes: TopicItem[] = useMemo(
        () => {
            const rawNotes = [
                { id: "v0.3.0", title: "v0.3.0", content: v0_3_0ReleaseNotes, filePath: "release-notes/v0.3.0.md" },
                { id: "v0.2.2", title: "v0.2.2", content: v0_2_2ReleaseNotes, filePath: "release-notes/v0.2.2.md" },
                { id: "v0.2.0", title: "v0.2.0", content: v0_2_0ReleaseNotes, filePath: "release-notes/v0.2.0.md" },
                { id: "v0.1.1", title: "v0.1.1", content: v0_1_1ReleaseNotes, filePath: "release-notes/v0.1.1.md" },
            ];
             return rawNotes.map(note => ({
                ...note,
                headings: extractHeadings(note.content)
            }));
        },
        []
    );

    const { topicId } = useParams();
    const navigate = useNavigate();
    const location = useLocation();

    const activeId = topicId || topics[0].id;

    const [expandedTopics, setExpandedTopics] = useState<Set<string>>(new Set([activeId]));
    const [releaseNotesOpen, setReleaseNotesOpen] = useState(false);

    useEffect(() => {
        if (topics.some(t => t.id === activeId)) {
            setExpandedTopics(prev => {
                const newSet = new Set(prev);
                newSet.add(activeId);
                return newSet;
            });
        } else if (releaseNotes.some(r => r.id === activeId)) {
            setReleaseNotesOpen(true);
        }
    }, [activeId, topics, releaseNotes]);

    useEffect(() => {
        if (location.hash) {
            const id = location.hash.replace('#', '');
            setTimeout(() => {
                const element = document.getElementById(id);
                if (element) {
                    element.scrollIntoView({ behavior: 'smooth', block: 'start' });
                }
            }, 100);
        }
    }, [location.hash, activeId]);

    const activeTopic = useMemo(() => {
        return (
            topics.find((topic) => topic.id === activeId) ??
            releaseNotes.find((note) => note.id === activeId) ??
            topics[0]
        );
    }, [activeId, topics, releaseNotes]);

    const handleTopicClick = (id: string) => {
        navigate(`/docs/${id}`);
        setExpandedTopics(prev => {
            const newSet = new Set(prev);
            newSet.add(id);
            return newSet;
        });
    };

    const toggleTopicExpand = (e: React.MouseEvent, id: string) => {
        e.stopPropagation();
        setExpandedTopics(prev => {
            const newSet = new Set(prev);
            if (newSet.has(id)) {
                newSet.delete(id);
            } else {
                newSet.add(id);
            }
            return newSet;
        });
    };

    const handleSubHeadingClick = (e: React.MouseEvent, topicId: string, headingId: string) => {
        e.stopPropagation();
        navigate(`/docs/${topicId}#${headingId}`);
    };

    return (
        <div className="min-h-screen bg-gradient-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
            <div className="px-6 py-16 sm:px-10 lg:px-16">
                <div className="flex flex-col gap-16 lg:flex-row lg:items-start">
                    <aside className="w-full max-w-sm shrink-0 space-y-6 lg:sticky lg:top-20 lg:h-[calc(100vh-5rem)] lg:overflow-y-auto lg:pr-4 custom-scrollbar">

                        {/* Topics Section */}
                        <div>
                            <h2 className="mb-4 text-xs font-semibold uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">
                                Topics
                            </h2>
                            <nav className="space-y-1">
                                {topics.map((topic) => {
                                    const isActiveTopic = topic.id === activeTopic?.id;
                                    const isExpanded = expandedTopics.has(topic.id);

                                    return (
                                        <div key={topic.id} className="space-y-1">
                                            <div className="relative">
                                                <button
                                                    type="button"
                                                    onClick={() => handleTopicClick(topic.id)}
                                                    className={cn(
                                                        "w-full rounded-md px-3 py-2 text-left text-sm font-medium transition focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-50 dark:focus-visible:ring-offset-slate-900 pr-8",
                                                        isActiveTopic
                                                            ? "bg-blue-500/10 text-blue-700 dark:bg-blue-500/20 dark:text-blue-100"
                                                            : "text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-slate-800"
                                                    )}
                                                    aria-pressed={isActiveTopic}
                                                >
                                                    {topic.title}
                                                </button>

                                                {topic.headings.length > 0 && (
                                                    <button
                                                        onClick={(e) => toggleTopicExpand(e, topic.id)}
                                                        className="absolute right-2 top-1/2 -translate-y-1/2 p-1 text-slate-400 hover:text-slate-600 dark:text-slate-500 dark:hover:text-slate-300"
                                                    >
                                                        {isExpanded ? (
                                                            <ChevronDown className="h-3.5 w-3.5" />
                                                        ) : (
                                                            <ChevronRight className="h-3.5 w-3.5" />
                                                        )}
                                                    </button>
                                                )}
                                            </div>

                                            {isExpanded && topic.headings.length > 0 && (
                                                <div className="ml-4 border-l border-slate-200 dark:border-slate-800 pl-2 space-y-0.5 my-1">
                                                    {topic.headings.map((heading, index) => {
                                                        // Показуємо рівні до 4 включно
                                                        if (heading.level > 4) return null;

                                                        const isHashActive = location.hash === `#${heading.id}` && isActiveTopic;

                                                        return (
                                                            <button
                                                                key={`${topic.id}-${heading.id}-${index}`}
                                                                type="button"
                                                                onClick={(e) => handleSubHeadingClick(e, topic.id, heading.id)}
                                                                className={cn(
                                                                    "block w-full text-left text-xs py-1.5 px-2 rounded-md transition-colors",
                                                                    isHashActive
                                                                        ? "text-blue-600 dark:text-blue-400 bg-blue-50 dark:bg-blue-900/20 font-medium"
                                                                        : "text-slate-500 hover:text-slate-800 hover:bg-slate-50 dark:text-slate-400 dark:hover:text-slate-200 dark:hover:bg-slate-800/50",
                                                                    heading.level === 3 && "pl-4",
                                                                    heading.level === 4 && "pl-6"
                                                                )}
                                                            >
                                                                {heading.title}
                                                            </button>
                                                        );
                                                    })}
                                                </div>
                                            )}
                                        </div>
                                    );
                                })}
                            </nav>
                        </div>

                        {/* Release Notes Section */}
                        <div className="pt-6 border-t border-slate-200 dark:border-slate-800">
                            <button
                                type="button"
                                onClick={() => setReleaseNotesOpen(!releaseNotesOpen)}
                                className="mb-3 flex w-full items-center justify-between text-xs font-semibold uppercase tracking-[0.2em] text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-slate-200 transition-colors"
                            >
                                <span>Release Notes</span>
                                {releaseNotesOpen ? (
                                    <ChevronDown className="h-3 w-3" />
                                ) : (
                                    <ChevronRight className="h-3 w-3" />
                                )}
                            </button>

                            {releaseNotesOpen && (
                                <nav className="space-y-1">
                                    {releaseNotes.map((note) => {
                                        const isActive = note.id === activeTopic?.id;
                                        return (
                                            <button
                                                key={note.id}
                                                type="button"
                                                onClick={() => handleTopicClick(note.id)}
                                                className={cn(
                                                    "w-full rounded-md px-3 py-2 text-left text-sm font-medium transition focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-50 dark:focus-visible:ring-offset-slate-900",
                                                    isActive
                                                        ? "bg-blue-500/10 text-blue-700 dark:bg-blue-500/20 dark:text-blue-100"
                                                        : "text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-slate-800"
                                                )}
                                                aria-pressed={isActive}
                                            >
                                                {note.title}
                                            </button>
                                        );
                                    })}
                                </nav>
                            )}
                        </div>
                    </aside>

                    <main className="flex-1 min-w-0">
                        {activeTopic && (
                            <article
                                key={activeTopic.id}
                                className="prose prose-slate max-w-none dark:prose-invert"
                            >
                                <MarkdownRenderer
                                    content={activeTopic.content}
                                />
                                {(() => {
                                    const filePath = getFilePathForTopic(
                                        activeTopic.id,
                                        topics,
                                        releaseNotes
                                    );
                                    return filePath != null && (
                                        <div className="mt-8 pt-6 border-t border-slate-200 dark:border-slate-700 not-prose">
                                            <a
                                                href={`https://github.com/RazorConsole/RazorConsole/edit/main/${filePath}`}
                                                target="_blank"
                                                rel="noopener noreferrer"
                                                className="flex items-center gap-2 text-sm text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-100 transition-colors"
                                            >
                                                <svg
                                                    className="w-4 h-4"
                                                    fill="currentColor"
                                                    viewBox="0 0 20 20"
                                                >
                                                    <path d="M13.586 3.586a2 2 0 112.828 2.828l-.793.793-2.828-2.828.793-.793zM11.379 5.793L3 14.172V17h2.828l8.38-8.379-2.83-2.828z" />
                                                </svg>
                                                Edit on GitHub
                                            </a>
                                        </div>
                                    );
                                })()}
                            </article>
                        )}
                    </main>
                </div>
            </div>
        </div>
      </div>
    </div>
  )
}
