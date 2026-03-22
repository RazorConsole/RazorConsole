import { useEffect, useMemo, useState } from "react"
import { useLoaderData, useNavigate, useParams, type LoaderFunctionArgs, type MetaFunction } from "react-router"
import ApiDocument from "@/components/api/ApiDocument"
import { apiItems, apiToc, type DocfxApiItem, type DocfxTocNode } from "@/data/api-docs"
import { ResponsiveSidebar } from "@/components/ui/ResponsiveSidebar"
import Sidebar from "@/components/api/Sidebar"

export const meta: MetaFunction<typeof loader> = ({ data }) => {
  const item = data?.activeItem;
  const title = item ? `${item.name} (${item.type}) | API Reference` : "API Reference | RazorConsole";
  
  return [
    { title },
    { name: "description", content: item?.summary || "Full API reference for RazorConsole classes, components, and utilities." },
  ];
};

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
export async function loader({ params }: LoaderFunctionArgs) {
  const item = params.uid ? apiItems[decodeURIComponent(params.uid)] : undefined;
  return { activeItem: item };
}

export default function ApiDocs() {
  const navigate = useNavigate()
  const params = useParams<{ uid: string }>()
  const [query, setQuery] = useState("")
  const docfxToc = apiToc
  const docfxItems = apiItems
  const decodedUid = params.uid ? decodeURIComponent(params.uid) : undefined
  const firstUid = useMemo(() => findFirstUid(docfxToc), [docfxToc])

  useEffect(() => {
    if (!decodedUid && firstUid) navigate(`/api/${encodeURIComponent(firstUid)}`, { replace: true })
  }, [decodedUid, firstUid, navigate])

  const activeItem = useLoaderData<{activeItem: DocfxApiItem | undefined}>().activeItem;
  const searchTerm = query.trim().toLowerCase()
  const searchResults = useMemo(() => {
    if (!searchTerm) return []
    return (Object.values(docfxItems) as DocfxApiItem[])
      .filter((item) => {
        const name = item.name.toLowerCase()
        const fullName = (item.fullName ?? "").toLowerCase()
        return name.includes(searchTerm) || fullName.includes(searchTerm)
      })
      .slice(0, 25)
  }, [docfxItems, searchTerm])

  const handleSelect = (uid: string) => {
    navigate(`/api/${encodeURIComponent(uid)}`)
    setQuery("")
  }

  return (
    <div className="min-h-screen bg-linear-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="px-6 py-16 sm:px-10 lg:px-16">
        <div className="flex flex-col lg:block">
          <ResponsiveSidebar breakpoint="lg" className="w-72 px-6 py-6">
            <Sidebar
              docfxToc={docfxToc}
              decodedUid={decodedUid}
              handleSelect={handleSelect}
              query={query}
              setQuery={setQuery}
              searchTerm={searchTerm}
              searchResults={searchResults}
            />
          </ResponsiveSidebar>
          <main className="min-w-0 flex-1">
            <ApiDocument item={activeItem} />
          </main>
        </div>
      </div>
    </div>
  )
}
