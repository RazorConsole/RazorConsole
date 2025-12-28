import type { DocfxTocNode } from "@/data/api-docs"
import { cn } from "@/lib/utils"

interface Props {
  nodes: DocfxTocNode[]
  activeUid?: string
  onSelect: (uid: string) => void
}

export default function TocTree({ nodes, activeUid, onSelect }: Props) {
  return (
    <ul className="space-y-1">
      {nodes.map((node) => {
        const isActive = node.uid === activeUid
        return (
          <li key={node.uid ?? node.name} className="space-y-1">
            {node.uid ? (
              <button
                type="button"
                onClick={() => node.uid && onSelect(node.uid)}
                className={cn(
                  "w-full rounded-md px-3 py-2 text-left text-sm font-medium transition focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-50 dark:focus-visible:ring-offset-slate-900",
                  isActive
                    ? "bg-blue-500/15 text-blue-700 dark:bg-blue-500/20 dark:text-blue-100"
                    : "text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-slate-800"
                )}
              >
                {node.name}
              </button>
            ) : (
              <div className="px-3 py-2 text-sm font-semibold tracking-[0.2em] text-slate-500 uppercase dark:text-slate-400">
                {node.name}
              </div>
            )}
            {node.items && node.items.length > 0 && (
              <div className="ml-4 border-l border-slate-200 pl-2 dark:border-slate-800">
                <TocTree nodes={node.items} activeUid={activeUid} onSelect={onSelect} />
              </div>
            )}
          </li>
        )
      })}
    </ul>
  )
}
