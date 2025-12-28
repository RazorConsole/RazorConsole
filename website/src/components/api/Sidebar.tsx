import type { DocfxTocNode } from "@/data/api-docs"
import { cn } from "@/lib/utils"
import { Search } from "lucide-react"
import TocTree from "@/components/api/TocTree"

interface Props {
  docfxToc: DocfxTocNode[]
  decodedUid: string | undefined
  handleSelect: (uid: string) => void
  query: string
  setQuery: (query: string) => void
  searchTerm: string
  searchResults: any[]
}

const Sidebar: React.FC<Props> = ({
  docfxToc,
  decodedUid,
  handleSelect,
  query,
  setQuery,
  searchTerm,
  searchResults,
}) => (
  <div className="space-y-3">
    <div className="relative">
      <Search className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-slate-400" />
      <input
        type="search"
        className="w-full rounded-lg border border-slate-200 bg-white py-2 pr-3 pl-9 text-sm text-slate-700 shadow-sm transition outline-none focus:border-blue-500 focus:ring-2 focus:ring-blue-200 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200 dark:focus:border-blue-400 dark:focus:ring-blue-500/40"
        placeholder="Search types..."
        value={query}
        onChange={(event) => setQuery(event.target.value)}
      />
    </div>
    {searchTerm ? (
      <div className="space-y-1">
        {searchResults.length === 0 && (
          <p className="px-3 py-2 text-sm text-slate-500 dark:text-slate-400">No matches found.</p>
        )}
        {searchResults.map((result) => (
          <button
            key={result.uid}
            type="button"
            onClick={() => handleSelect(result.uid)}
            className={cn(
              "w-full rounded-md px-3 py-2 text-left text-sm transition focus:outline-none focus-visible:ring-2 focus-visible:ring-blue-500 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-50 dark:focus-visible:ring-offset-slate-900",
              result.uid === decodedUid
                ? "bg-blue-500/15 text-blue-700 dark:bg-blue-500/20 dark:text-blue-100"
                : "text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-slate-800"
            )}
          >
            <span className="block font-medium">{result.name}</span>
            <span className="block text-xs text-slate-500 dark:text-slate-400">
              {result.fullName ?? result.uid}
            </span>
          </button>
        ))}
      </div>
    ) : (
      <TocTree nodes={docfxToc} activeUid={decodedUid} onSelect={handleSelect} />
    )}
  </div>
)

export default Sidebar
