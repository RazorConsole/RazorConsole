import { useMemo, useState } from "react"
import { useLoaderData, useNavigate, useParams, type LoaderFunctionArgs, type MetaFunction } from "react-router"
import ApiDocument from "@/components/api/ApiDocument"
import { apiItems, apiToc, type DocfxApiItem } from "@/data/api-docs"
import { ResponsiveSidebar } from "@/components/ui/ResponsiveSidebar"
import Sidebar from "@/components/api/Sidebar"
import { getPageUrl } from "@/lib/utils"
import { apiDescription } from "@/lib/doc-utils"

export const meta: MetaFunction<typeof loader> = ({ data, matches, location }) => {
  const rootMeta = matches.find((m) => m.id === "root")?.meta || [];
  const pageUrl = getPageUrl(location.pathname);
  
  const item = data?.activeItem;
  const title = item ? `${item.name} (${item.type ?? "Type"}) | RazorConsole API` : "API Reference | RazorConsole";
  const description = item ? apiDescription(item) : "Browse the RazorConsole .NET API: Razor components, input events, rendering services, and utilities.";
  
  return [
    ...rootMeta,
    { title },
    { property: "og:url", content: pageUrl },
    { name: "description", content: description },
    { property: "og:title", content: title },
    { property: "og:description", content: description },
  ];
};

export async function loader({ params }: LoaderFunctionArgs) {
  const item = params.uid ? apiItems[decodeURIComponent(params.uid)] : undefined;
  if (params.uid && !item) throw new Response("Not Found", { status: 404 });
  return { activeItem: item };
}

export default function ApiDocs() {
  const navigate = useNavigate()
  const params = useParams<{ uid: string }>()
  const [query, setQuery] = useState("")
  const docfxToc = apiToc
  const docfxItems = apiItems
  const decodedUid = params.uid ? decodeURIComponent(params.uid) : undefined

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
    navigate(`/api/${encodeURIComponent(uid)}/`)
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
