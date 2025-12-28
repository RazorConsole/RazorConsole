import {
  type MemberCategory,
  categorizeMember,
  CATEGORY_ORDER,
  getCategoryIcon,
} from "@/lib/categories"
import { sanitizeDocText } from "@/lib/doc-utils"
import type Parameter from "@/types/components/parameter"
import { Settings, Search } from "lucide-react"
import { useState, useMemo } from "react"
import { Fragment } from "react/jsx-runtime"
import { TypeLink } from "../ui/TypeLink"

export default function ParametersTable({ parameters }: { parameters: Parameter[] }) {
  const [searchQuery, setSearchQuery] = useState("")

  const filteredParams = useMemo(() => {
    if (!searchQuery.trim()) return parameters
    const query = searchQuery.toLowerCase()
    return parameters.filter(
      (p) =>
        p.name.toLowerCase().includes(query) ||
        p.type.toLowerCase().includes(query) ||
        (p.description ?? "").toLowerCase().includes(query)
    )
  }, [parameters, searchQuery])

  const groupedParams = useMemo(() => {
    const groups = new Map<MemberCategory, Parameter[]>()
    for (const param of filteredParams) {
      const category = categorizeMember(param.name, param.type, param.description)
      if (!groups.has(category)) {
        groups.set(category, [])
      }
      groups.get(category)?.push(param)
    }

    return CATEGORY_ORDER.filter((cat) => groups.has(cat)).map((cat) => ({
      category: cat,
      params: groups.get(cat) ?? [],
    }))
  }, [filteredParams])

  return (
    <section className="space-y-4">
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-2">
          <span className="text-blue-600 dark:text-blue-400">
            <Settings className="h-5 w-5" />
          </span>
          <h3 className="text-xl font-semibold text-slate-900 dark:text-slate-100">Parameters</h3>
          <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600 dark:bg-slate-800 dark:text-slate-400">
            {parameters.length}
          </span>
        </div>

        {parameters.length > 3 && (
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              type="search"
              className="w-full rounded-lg border border-slate-200 bg-white py-2 pr-3 pl-9 text-sm text-slate-700 shadow-sm transition outline-none focus:border-blue-500 focus:ring-2 focus:ring-blue-200 sm:w-64 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200 dark:focus:border-blue-400 dark:focus:ring-blue-500/40"
              placeholder="Search parameters..."
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
            />
          </div>
        )}
      </div>

      <div className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-900">
        <table className="min-w-full divide-y divide-slate-200 text-sm dark:divide-slate-800">
          <thead className="bg-slate-50 text-left font-medium text-slate-600 dark:bg-slate-950 dark:text-slate-400">
            <tr>
              <th scope="col" className="px-4 py-3 font-semibold">
                Name
              </th>
              <th scope="col" className="px-4 py-3 font-semibold">
                Type
              </th>
              <th scope="col" className="px-4 py-3 font-semibold">
                Default
              </th>
              <th scope="col" className="px-4 py-3 font-semibold">
                Description
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
            {groupedParams.map(({ category, params }) => (
              <Fragment key={category}>
                {/* Category header row */}
                <tr className="bg-slate-50/50 dark:bg-slate-800/30">
                  <td colSpan={4} className="px-4 py-2">
                    <div className="flex items-center gap-2 text-xs font-semibold tracking-wider text-slate-500 uppercase dark:text-slate-400">
                      {getCategoryIcon(category)}
                      {category}
                    </div>
                  </td>
                </tr>
                {/* Parameter rows */}
                {params.map((param, idx) => (
                  <tr
                    key={`${category}-${idx}`}
                    className="group align-top hover:bg-slate-50 dark:hover:bg-slate-800/50"
                  >
                    <td className="px-4 py-3">
                      <code className="font-mono text-xs font-medium text-slate-900 dark:text-slate-100">
                        {param.name}
                      </code>
                    </td>
                    <td className="px-4 py-3">
                      <TypeLink type={param.type} />
                    </td>
                    <td className="px-4 py-3">
                      {param.default ? (
                        <code className="font-mono text-xs text-slate-600 dark:text-slate-400">
                          {param.default}
                        </code>
                      ) : (
                        <span className="text-slate-400">—</span>
                      )}
                    </td>
                    <td className="px-4 py-3 text-slate-600 dark:text-slate-300">
                      {sanitizeDocText(param.description) ?? "—"}
                    </td>
                  </tr>
                ))}
              </Fragment>
            ))}
            {filteredParams.length === 0 && (
              <tr>
                <td
                  colSpan={4}
                  className="px-4 py-8 text-center text-slate-500 dark:text-slate-400"
                >
                  No parameters found matching "{searchQuery}"
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}
