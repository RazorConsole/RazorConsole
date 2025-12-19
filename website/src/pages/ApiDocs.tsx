import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import ApiDocument from '@/components/ApiDocument'
import { apiItems, apiToc, type DocfxApiItem, type DocfxTocNode } from '@/data/api-docs'
import { Search } from 'lucide-react'
import { cn } from '@/lib/utils'

function findFirstUid(nodes: DocfxTocNode[]): string | undefined {
  for (const node of nodes) {
    if (node.uid) {
      return node.uid
    }
    if (node.items) {
      const nested = findFirstUid(node.items)
      if (nested) {
        return nested
      }
    }
  }
  return undefined
}

function TocTree({
  nodes,
  activeUid,
  onSelect
}: {
  nodes: DocfxTocNode[]
  activeUid?: string
  onSelect: (uid: string) => void
}) {
  return (
    <ul className="space-y-1">
      {nodes.map(node => {
        const key = node.uid ?? node.name
        const isActive = node.uid === activeUid
        const isClickable = Boolean(node.uid)

        return (
          <li key={key} className="space-y-1">
            {isClickable ? (
              <button
                type="button"
                onClick={() => node.uid && onSelect(node.uid)}
                className={cn(
                  'w-full rounded-md px-3 py-2 text-left text-sm font-medium transition focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-50 dark:focus-visible:ring-offset-slate-900',
                  isActive
                    ? 'bg-blue-500/15 text-blue-700 dark:bg-blue-500/20 dark:text-blue-100'
                    : 'text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-slate-800'
                )}
              >
                {node.name}
              </button>
            ) : (
              <div className="px-3 py-2 text-sm font-semibold uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">
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

export default function ApiDocs() {
  const navigate = useNavigate()
  const params = useParams<{ uid: string }>()
  const [query, setQuery] = useState('')

  const docfxToc = apiToc
  const docfxItems = apiItems

  const decodedUid = params.uid ? decodeURIComponent(params.uid) : undefined

  const firstUid = useMemo(() => findFirstUid(docfxToc), [docfxToc])

  useEffect(() => {
    if (!decodedUid && firstUid) {
      navigate(`/api/${encodeURIComponent(firstUid)}`, { replace: true })
    }
  }, [decodedUid, firstUid, navigate])

  const activeItem: DocfxApiItem | undefined = decodedUid ? docfxItems[decodedUid] : undefined

  const searchTerm = query.trim().toLowerCase()
  const searchResults = useMemo(() => {
    if (!searchTerm) {
      return [] as DocfxApiItem[]
    }
    return (Object.values(docfxItems) as DocfxApiItem[])
      .filter(item => {
        const name = item.name.toLowerCase()
        const fullName = (item.fullName ?? '').toLowerCase()
        return name.includes(searchTerm) || fullName.includes(searchTerm)
      })
      .slice(0, 25)
  }, [docfxItems, searchTerm])

  const handleSelect = (uid: string) => {
    navigate(`/api/${encodeURIComponent(uid)}`)
    setQuery('')
  }

  return (
    <div className="min-h-screen bg-gradient-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="px-6 py-16 sm:px-10 lg:px-16">
        <div className="flex flex-col gap-16 lg:flex-row lg:items-start">
          <aside className="w-full max-w-sm shrink-0 space-y-6 lg:sticky lg:top-20 lg:h-[calc(100vh-5rem)] lg:overflow-y-auto lg:pr-4 custom-scrollbar">
            <div className="space-y-3">
              <div className="relative">
                <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                <input
                  type="search"
                  className="w-full rounded-lg border border-slate-200 bg-white py-2 pl-9 pr-3 text-sm text-slate-700 shadow-sm outline-none transition focus:border-blue-500 focus:ring-2 focus:ring-blue-200 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200 dark:focus:border-blue-400 dark:focus:ring-blue-500/40"
                  placeholder="Search types..."
                  value={query}
                  onChange={event => setQuery(event.target.value)}
                />
              </div>

              {searchTerm ? (
                <div className="space-y-1">
                  {searchResults.length === 0 && (
                    <p className="px-3 py-2 text-sm text-slate-500 dark:text-slate-400">No matches found.</p>
                  )}
                  {searchResults.map(result => {
                    const isActive = result.uid === decodedUid
                    return (
                      <button
                        key={result.uid}
                        type="button"
                        onClick={() => handleSelect(result.uid)}
                        className={cn(
                          'w-full rounded-md px-3 py-2 text-left text-sm transition focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-50 dark:focus-visible:ring-offset-slate-900',
                          isActive
                            ? 'bg-blue-500/15 text-blue-700 dark:bg-blue-500/20 dark:text-blue-100'
                            : 'text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-slate-800'
                        )}
                      >
                        <span className="block font-medium">{result.name}</span>
                        <span className="block text-xs text-slate-500 dark:text-slate-400">{result.fullName ?? result.uid}</span>
                      </button>
                    )
                  })}
                </div>
              ) : (
                <TocTree nodes={docfxToc} activeUid={decodedUid} onSelect={handleSelect} />
              )}
            </div>
          </aside>

          <main className="flex-1 min-w-0">
            <ApiDocument item={activeItem} />
          </main>
        </div>
      </div>
    </div>
  )
}
