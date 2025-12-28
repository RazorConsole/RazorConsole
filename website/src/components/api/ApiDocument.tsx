import { useMemo, useState, Fragment } from "react"
import { Link } from "react-router-dom"
import type { DocfxApiItem, DocfxApiMember, DocfxSyntaxParameter } from "@/data/api-docs"
import { cn } from "@/lib/utils"
import { TypeLink } from "@/components/ui/TypeLink"
import { Search, ChevronRight, BookOpen, Code2, Settings, Zap, Box, FileCode } from "lucide-react"
import {
  type MemberCategory,
  getCategoryIcon,
  categorizeMember,
  CATEGORY_ORDER,
} from "@/lib/categories"
import { sanitizeDocText } from "@/lib/doc-utils"

interface ApiDocumentProps {
  item?: DocfxApiItem
}

function categorizeMemberFromDocfx(member: DocfxApiMember): MemberCategory {
  return categorizeMember(member.name, member.type ?? "", member.summary)
}

function SyntaxBlock({ code }: { code?: string }) {
  if (!code) {
    return null
  }

  return (
    <div className="rounded-lg border border-slate-200 bg-slate-950 text-sm shadow-inner dark:border-slate-700">
      <pre className="overflow-x-auto p-4">
        <code className="font-mono text-[13px] text-slate-200">{code}</code>
      </pre>
    </div>
  )
}

function ParameterTable({ parameters }: { parameters?: DocfxSyntaxParameter[] }) {
  if (!parameters || parameters.length === 0) {
    return null
  }

  return (
    <div className="overflow-x-auto rounded-lg border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-900">
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
              Description
            </th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
          {parameters.map((param) => (
            <tr key={param.id} className="align-top hover:bg-slate-50 dark:hover:bg-slate-800/50">
              <td className="px-4 py-3">
                <code className="font-mono text-xs font-medium text-slate-900 dark:text-slate-100">
                  {param.name ?? param.id}
                </code>
              </td>
              <td className="px-4 py-3">
                <TypeLink type={param.type} />
              </td>
              <td className="px-4 py-3 text-slate-600 dark:text-slate-300">
                {sanitizeDocText(param.description) ?? "—"}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

interface MemberTableProps {
  members: DocfxApiMember[]
  title: string
  icon: React.ReactNode
  groupByCategory?: boolean
  sectionId: string
}

function MemberTable({
  members,
  title,
  icon,
  groupByCategory = false,
  sectionId,
}: MemberTableProps) {
  const [searchQuery, setSearchQuery] = useState("")

  const filteredMembers = useMemo(() => {
    if (!searchQuery.trim()) {
      return members
    }
    const query = searchQuery.toLowerCase()
    return members.filter(
      (member) =>
        member.name.toLowerCase().includes(query) ||
        (member.summary ?? "").toLowerCase().includes(query)
    )
  }, [members, searchQuery])

  const groupedMembers = useMemo(() => {
    if (!groupByCategory) {
      return [{ category: null as MemberCategory | null, members: filteredMembers }]
    }

    const groups = new Map<MemberCategory, DocfxApiMember[]>()
    for (const member of filteredMembers) {
      const category = categorizeMemberFromDocfx(member)
      if (!groups.has(category)) {
        groups.set(category, [])
      }
      groups.get(category)?.push(member)
    }

    return CATEGORY_ORDER.filter((cat) => groups.has(cat)).map((cat) => ({
      category: cat,
      members: groups.get(cat) ?? [],
    }))
  }, [filteredMembers, groupByCategory])

  return (
    <section id={sectionId} className="scroll-mt-20">
      <div className="mb-4 flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-2">
          <span className="text-blue-600 dark:text-blue-400">{icon}</span>
          <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-100">{title}</h2>
          <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600 dark:bg-slate-800 dark:text-slate-400">
            {members.length}
          </span>
        </div>

        {members.length > 3 && (
          <div className="relative">
            <Search className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-slate-400" />
            <input
              type="search"
              className="w-full rounded-lg border border-slate-200 bg-white py-2 pr-3 pl-9 text-sm text-slate-700 shadow-sm transition outline-none focus:border-blue-500 focus:ring-2 focus:ring-blue-200 sm:w-64 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200 dark:focus:border-blue-400 dark:focus:ring-blue-500/40"
              placeholder={`Search ${title.toLowerCase()}...`}
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
                Description
              </th>
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
            {groupedMembers.map(({ category, members: groupMembers }) => (
              <Fragment key={`group-${category ?? "default"}`}>
                {category && (
                  <tr className="bg-slate-50/50 dark:bg-slate-800/30">
                    <td colSpan={3} className="px-4 py-2">
                      <div className="flex items-center gap-2 text-xs font-semibold tracking-wider text-slate-500 uppercase dark:text-slate-400">
                        {getCategoryIcon(category)}
                        {category}
                      </div>
                    </td>
                  </tr>
                )}
                {groupMembers.map((member) => {
                  const summary = sanitizeDocText(member.summary)
                  const returnType = member.syntax?.return?.type ?? member.type

                  return (
                    <tr
                      key={member.uid}
                      className="group align-top hover:bg-slate-50 dark:hover:bg-slate-800/50"
                    >
                      <td className="px-4 py-3">
                        <code className="font-mono text-xs font-medium text-slate-900 dark:text-slate-100">
                          {member.name}
                        </code>
                        {member.syntax?.parameters && member.syntax.parameters.length > 0 && (
                          <span className="text-slate-400">(</span>
                        )}
                        {member.syntax?.parameters && member.syntax.parameters.length > 0 && (
                          <span className="text-slate-400">)</span>
                        )}
                      </td>
                      <td className="px-4 py-3">
                        <TypeLink type={returnType} />
                      </td>
                      <td className="px-4 py-3 text-slate-600 dark:text-slate-300">
                        {summary ?? "—"}
                      </td>
                    </tr>
                  )
                })}
              </Fragment>
            ))}
            {filteredMembers.length === 0 && (
              <tr>
                <td
                  colSpan={3}
                  className="px-4 py-8 text-center text-slate-500 dark:text-slate-400"
                >
                  No members found matching "{searchQuery}"
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </section>
  )
}

// Breadcrumb component
function Breadcrumbs({ item }: { item: DocfxApiItem }) {
  const parts = item.fullName?.split(".") ?? [item.name]

  return (
    <nav className="flex items-center gap-1 text-sm text-slate-500 dark:text-slate-400">
      <Link to="/api" className="hover:text-blue-600 dark:hover:text-blue-400">
        API
      </Link>
      {parts.map((part, index) => (
        <span key={index} className="flex items-center gap-1">
          <ChevronRight className="h-4 w-4" />
          {index === parts.length - 1 ? (
            <span className="font-medium text-slate-900 dark:text-slate-100">{part}</span>
          ) : (
            <span>{part}</span>
          )}
        </span>
      ))}
    </nav>
  )
}

// Table of Contents sidebar
function TableOfContents({
  sections,
}: {
  sections: { id: string; title: string; count: number }[]
}) {
  return (
    <nav className="sticky top-20 space-y-1">
      <p className="mb-3 text-xs font-semibold tracking-wider text-slate-500 uppercase dark:text-slate-400">
        On this page
      </p>
      {sections.map((section) => (
        <a
          key={section.id}
          href={`#${section.id}`}
          className="flex items-center justify-between rounded-md px-3 py-1.5 text-sm text-slate-600 transition hover:bg-slate-100 hover:text-slate-900 dark:text-slate-400 dark:hover:bg-slate-800 dark:hover:text-slate-100"
        >
          <span>{section.title}</span>
          <span className="text-xs text-slate-400">{section.count}</span>
        </a>
      ))}
    </nav>
  )
}

// Get icon for member type
function getMemberTypeIcon(type?: string) {
  switch (type?.toLowerCase()) {
    case "constructor":
      return <Code2 className="h-5 w-5" />
    case "method":
      return <Code2 className="h-5 w-5" />
    case "property":
      return <Settings className="h-5 w-5" />
    case "event":
      return <Zap className="h-5 w-5" />
    case "field":
      return <Box className="h-5 w-5" />
    default:
      return <FileCode className="h-5 w-5" />
  }
}

// Get type badge color
function getTypeBadgeColor(type?: string) {
  switch (type?.toLowerCase()) {
    case "class":
      return "border-blue-200 bg-blue-50 text-blue-700 dark:border-blue-500/40 dark:bg-blue-500/10 dark:text-blue-300"
    case "interface":
      return "border-purple-200 bg-purple-50 text-purple-700 dark:border-purple-500/40 dark:bg-purple-500/10 dark:text-purple-300"
    case "enum":
      return "border-orange-200 bg-orange-50 text-orange-700 dark:border-orange-500/40 dark:bg-orange-500/10 dark:text-orange-300"
    case "struct":
      return "border-teal-200 bg-teal-50 text-teal-700 dark:border-teal-500/40 dark:bg-teal-500/10 dark:text-teal-300"
    case "delegate":
      return "border-rose-200 bg-rose-50 text-rose-700 dark:border-rose-500/40 dark:bg-rose-500/10 dark:text-rose-300"
    case "namespace":
      return "border-slate-200 bg-slate-50 text-slate-700 dark:border-slate-500/40 dark:bg-slate-500/10 dark:text-slate-300"
    default:
      return "border-slate-200 bg-slate-50 text-slate-700 dark:border-slate-500/40 dark:bg-slate-500/10 dark:text-slate-300"
  }
}

// Check if this type is a component (has a corresponding component page)
function getComponentLink(name: string, namespace?: string): string | null {
  // Components in RazorConsole.Components namespace may have a component page
  if (namespace === "RazorConsole.Components") {
    const componentNames = [
      "Align",
      "BarChart",
      "Border",
      "BreakdownChart",
      "Columns",
      "Figlet",
      "Grid",
      "Markdown",
      "Markup",
      "Newline",
      "Padder",
      "Panel",
      "Rows",
      "Scrollable",
      "Select",
      "SpectreCanvas",
      "SpectreTable",
      "Spinner",
      "StepChart",
      "SyntaxHighlighter",
      "TextButton",
      "TextInput",
    ]

    const baseName = name.split("<")[0] // Handle generic types like Scrollable<TItem>
    if (componentNames.includes(baseName)) {
      return `/components/${baseName.toLowerCase()}`
    }
  }
  return null
}

export default function ApiDocument({ item }: ApiDocumentProps) {
  const memberGroups = useMemo(() => {
    if (!item?.members) {
      return []
    }

    const groups = new Map<string, DocfxApiMember[]>()
    for (const member of item.members) {
      const bucket = member.type ?? "Member"
      if (!groups.has(bucket)) {
        groups.set(bucket, [])
      }
      groups.get(bucket)?.push(member)
    }

    const order = ["Constructor", "Property", "Method", "Event", "Field", "Member"]

    return Array.from(groups.entries()).sort((a, b) => {
      const indexA = order.indexOf(a[0])
      const indexB = order.indexOf(b[0])
      if (indexA === -1 && indexB === -1) {
        return a[0].localeCompare(b[0])
      }
      if (indexA === -1) {
        return 1
      }
      if (indexB === -1) {
        return -1
      }
      if (indexA === indexB) {
        return a[0].localeCompare(b[0])
      }
      return indexA - indexB
    })
  }, [item?.members])

  const tocSections = useMemo(() => {
    const sections: { id: string; title: string; count: number }[] = []

    if (item?.syntax?.content || item?.syntax?.contentCs) {
      sections.push({ id: "definition", title: "Definition", count: 1 })
    }

    for (const [groupName, members] of memberGroups) {
      const plural = groupName === "Property" ? "Properties" : `${groupName}s`
      sections.push({
        id: groupName.toLowerCase(),
        title: plural,
        count: members.length,
      })
    }

    return sections
  }, [item, memberGroups])

  if (!item) {
    return (
      <div className="rounded-xl border border-dashed border-slate-300 bg-white/50 p-10 text-center text-sm text-slate-500 dark:border-slate-700 dark:bg-slate-900/70 dark:text-slate-400">
        Select an API type from the navigation to see its documentation.
      </div>
    )
  }

  const code = item.syntax?.contentCs ?? item.syntax?.content
  const summary = sanitizeDocText(item.summary)
  const remarks = sanitizeDocText(item.remarks)
  const componentLink = getComponentLink(item.name, item.namespace)

  return (
    <div className="flex gap-8">
      {/* Main Content */}
      <div className="min-w-0 flex-1 space-y-8">
        {/* Header Section */}
        <section className="space-y-6">
          {/* Breadcrumbs */}
          <Breadcrumbs item={item} />

          {/* Title and Badge */}
          <div className="space-y-3">
            <div className="flex flex-wrap items-center gap-3">
              <h1 className="text-3xl font-bold tracking-tight text-slate-900 dark:text-slate-50">
                {item.name}
              </h1>
              <span
                className={cn(
                  "rounded-full border px-3 py-1 text-xs font-semibold tracking-widest uppercase",
                  getTypeBadgeColor(item.type)
                )}
              >
                {item.type ?? "Type"}
              </span>
            </div>

            {/* Description */}
            {summary && <p className="text-lg text-slate-600 dark:text-slate-300">{summary}</p>}
          </div>

          {/* Component Page Link */}
          {componentLink && (
            <div className="flex items-start gap-3 rounded-lg border border-blue-200 bg-blue-50 p-4 dark:border-blue-500/30 dark:bg-blue-500/10">
              <BookOpen className="mt-0.5 h-5 w-5 shrink-0 text-blue-600 dark:text-blue-400" />
              <div className="space-y-1">
                <p className="text-sm text-blue-900 dark:text-blue-100">
                  For examples and usage details, see the component documentation.
                </p>
                <Link
                  to={componentLink}
                  className="inline-flex items-center gap-1 text-sm font-medium text-blue-700 hover:text-blue-800 hover:underline dark:text-blue-300 dark:hover:text-blue-200"
                >
                  View {item.name} Component
                  <ChevronRight className="h-4 w-4" />
                </Link>
              </div>
            </div>
          )}

          {/* Metadata */}
          <div className="flex flex-wrap gap-4 text-sm text-slate-500 dark:text-slate-400">
            {item.namespace && (
              <div>
                <span className="font-medium">Namespace:</span>{" "}
                <code className="font-mono text-violet-600 dark:text-violet-400">
                  {item.namespace}
                </code>
              </div>
            )}
            {item.assemblies && item.assemblies.length > 0 && (
              <div>
                <span className="font-medium">Assembly:</span>{" "}
                <code className="font-mono text-violet-600 dark:text-violet-400">
                  {item.assemblies.join(", ")}
                </code>
              </div>
            )}
          </div>
        </section>

        {/* Definition */}
        {code && (
          <section id="definition" className="scroll-mt-20 space-y-4">
            <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-100">Definition</h2>
            <SyntaxBlock code={code} />
            <ParameterTable parameters={item.syntax?.parameters} />

            {item.syntax?.return?.type && (
              <div className="rounded-lg border border-slate-200 bg-slate-50 p-4 text-sm dark:border-slate-700 dark:bg-slate-800/70">
                <span className="font-semibold text-slate-900 dark:text-slate-100">Returns:</span>
                <span className="ml-2">
                  <TypeLink type={item.syntax.return.type} />
                </span>
                {item.syntax.return.description && (
                  <span className="ml-2 text-slate-600 dark:text-slate-300">
                    {sanitizeDocText(item.syntax.return.description)}
                  </span>
                )}
              </div>
            )}
          </section>
        )}

        {/* Remarks */}
        {remarks && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-4 text-sm text-amber-900 dark:border-amber-700/60 dark:bg-amber-500/10 dark:text-amber-200">
            <span className="font-semibold">Remarks:</span>
            <span className="ml-2">{remarks}</span>
          </div>
        )}

        {/* Member Groups (skip for Namespace types - they have their own grid view) */}
        {item.type !== "Namespace" &&
          memberGroups.map(([groupName, members]) => {
            const plural = groupName === "Property" ? "Properties" : `${groupName}s`
            const shouldGroupByCategory = groupName === "Property"

            return (
              <MemberTable
                key={groupName}
                members={members}
                title={plural}
                icon={getMemberTypeIcon(groupName)}
                groupByCategory={shouldGroupByCategory}
                sectionId={groupName.toLowerCase()}
              />
            )
          })}

        {/* Namespace members (for namespace items) */}
        {item.type === "Namespace" && item.children && item.children.length > 0 && (
          <section id="members" className="scroll-mt-20 space-y-4">
            <div className="flex items-center gap-2">
              <span className="text-blue-600 dark:text-blue-400">
                <FileCode className="h-5 w-5" />
              </span>
              <h2 className="text-xl font-semibold text-slate-900 dark:text-slate-100">Members</h2>
              <span className="rounded-full bg-slate-100 px-2 py-0.5 text-xs font-medium text-slate-600 dark:bg-slate-800 dark:text-slate-400">
                {item.members?.length ?? 0}
              </span>
            </div>

            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {item.members?.map((member) => (
                <Link
                  key={member.uid}
                  to={`/api/${encodeURIComponent(member.uid)}`}
                  className="group rounded-lg border border-slate-200 bg-white p-4 shadow-sm transition hover:border-blue-300 hover:shadow-md dark:border-slate-800 dark:bg-slate-900 dark:hover:border-blue-700"
                >
                  <div className="flex items-center justify-between">
                    <h3 className="font-semibold text-slate-900 group-hover:text-blue-600 dark:text-slate-100 dark:group-hover:text-blue-400">
                      {member.name}
                    </h3>
                    <span
                      className={cn(
                        "rounded px-2 py-0.5 text-[10px] font-semibold uppercase",
                        getTypeBadgeColor(member.type)
                      )}
                    >
                      {member.type ?? "Type"}
                    </span>
                  </div>
                  {sanitizeDocText(member.summary) && (
                    <p className="mt-2 line-clamp-2 text-sm text-slate-500 dark:text-slate-400">
                      {sanitizeDocText(member.summary)}
                    </p>
                  )}
                </Link>
              ))}
            </div>
          </section>
        )}
      </div>

      {/* Table of Contents Sidebar */}
      {tocSections.length > 1 && (
        <aside className="hidden w-48 shrink-0 xl:block">
          <TableOfContents sections={tocSections} />
        </aside>
      )}
    </div>
  )
}
