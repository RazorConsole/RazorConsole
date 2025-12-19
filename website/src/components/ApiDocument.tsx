import { useMemo } from 'react'
import type { DocfxApiItem, DocfxApiMember, DocfxSyntaxParameter } from '@/data/api-docs'

interface ApiDocumentProps {
  item?: DocfxApiItem
}

function sanitizeDocText(value?: string) {
  if (!value) {
    return undefined
  }
  const withoutTags = value.replace(/<[^>]+>/g, ' ')
  const condensed = withoutTags.replace(/\s+/g, ' ').trim()
  return condensed.length > 0 ? condensed : undefined
}

function SyntaxBlock({ code }: { code?: string }) {
  if (!code) {
    return null
  }

  return (
    <div className="rounded-lg border border-slate-200 bg-slate-950/95 text-sm text-slate-200 shadow-inner dark:border-slate-700">
      <pre className="overflow-x-auto p-4">
        <code>{code}</code>
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
            <th scope="col" className="px-4 py-2">Name</th>
            <th scope="col" className="px-4 py-2">Type</th>
            <th scope="col" className="px-4 py-2">Description</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
          {parameters.map(param => (
            <tr key={param.id} className="align-top">
              <td className="px-4 py-2 font-mono text-xs text-slate-900 dark:text-slate-100">{param.name ?? param.id}</td>
              <td className="px-4 py-2 font-mono text-xs text-emerald-600 dark:text-emerald-400">{param.type ?? '—'}</td>
              <td className="px-4 py-2 text-slate-700 dark:text-slate-300">{sanitizeDocText(param.description) ?? '—'}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function MemberCard({ member }: { member: DocfxApiMember }) {
  const code = member.syntax?.contentCs ?? member.syntax?.content
  const parameters = member.syntax?.parameters
  const returnInfo = member.syntax?.return
  const summary = sanitizeDocText(member.summary)
  const remarks = sanitizeDocText(member.remarks)

  return (
    <section id={member.uid} className="space-y-4 rounded-xl border border-slate-200 bg-white p-4 shadow-sm dark:border-slate-800 dark:bg-slate-900">
      <header className="space-y-1">
        <h3 className="font-semibold text-slate-900 dark:text-slate-100">{member.nameWithType ?? member.name}</h3>
        <p className="text-xs uppercase tracking-[0.2em] text-slate-500 dark:text-slate-400">{member.type ?? 'Member'}</p>
      </header>

      <SyntaxBlock code={code} />

      {summary && (
        <p className="text-sm text-slate-700 dark:text-slate-300">{summary}</p>
      )}

      <ParameterTable parameters={parameters} />

      {returnInfo?.type && (
        <div className="rounded-lg border border-slate-200 bg-slate-50 p-3 text-sm text-slate-700 dark:border-slate-700 dark:bg-slate-800/70 dark:text-slate-200">
          <span className="font-medium text-slate-900 dark:text-slate-100">Returns:</span>
          <span className="ml-2 font-mono text-emerald-600 dark:text-emerald-400">{returnInfo.type}</span>
          {returnInfo.description && (
            <span className="ml-2 text-slate-600 dark:text-slate-300">{sanitizeDocText(returnInfo.description)}</span>
          )}
        </div>
      )}

      {remarks && (
        <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-700/60 dark:bg-amber-500/10 dark:text-amber-200">
          <span className="font-medium">Remarks:</span>
          <span className="ml-2">{remarks}</span>
        </div>
      )}

      {member.examples && member.examples.length > 0 && (
        <div className="space-y-2">
          <p className="text-sm font-medium text-slate-900 dark:text-slate-100">Examples</p>
          {member.examples.map((example, index) => (
            <SyntaxBlock key={index} code={example ?? undefined} />
          ))}
        </div>
      )}
    </section>
  )
}

export default function ApiDocument({ item }: ApiDocumentProps) {
  const memberGroups = useMemo(() => {
    if (!item?.members) {
      return []
    }

    const groups = new Map<string, DocfxApiMember[]>()
    for (const member of item.members) {
      const bucket = member.type ?? 'Member'
      if (!groups.has(bucket)) {
        groups.set(bucket, [])
      }
      groups.get(bucket)?.push(member)
    }

    const order = ['Constructor', 'Method', 'Property', 'Field', 'Event', 'Member']

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

  return (
    <div className="space-y-8">
      <section className="space-y-4 rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-slate-800 dark:bg-slate-900">
        <header className="space-y-2">
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="text-3xl font-bold tracking-tight text-slate-900 dark:text-slate-50">{item.name}</h1>
            <span className="rounded-full border border-blue-200 bg-blue-50 px-3 py-1 text-xs font-semibold uppercase tracking-widest text-blue-700 dark:border-blue-500/40 dark:bg-blue-500/10 dark:text-blue-200">
              {item.type ?? 'Type'}
            </span>
          </div>
          <div className="text-sm text-slate-500 dark:text-slate-400">
            <span className="font-mono text-violet-600 dark:text-violet-300">{item.fullName ?? item.uid}</span>
            {item.namespace && (
              <span className="ml-3 text-slate-600 dark:text-slate-300">Namespace: {item.namespace}</span>
            )}
            {item.assemblies && item.assemblies.length > 0 && (
              <span className="ml-3 text-slate-600 dark:text-slate-300">Assembly: {item.assemblies.join(', ')}</span>
            )}
          </div>
        </header>

        {summary && (
          <p className="text-base text-slate-700 dark:text-slate-200">{summary}</p>
        )}

        <SyntaxBlock code={code} />

        <ParameterTable parameters={item.syntax?.parameters} />

        {item.syntax?.return?.type && (
          <div className="rounded-lg border border-slate-200 bg-slate-50 p-4 text-sm text-slate-700 dark:border-slate-700 dark:bg-slate-800/70 dark:text-slate-200">
            <span className="font-medium text-slate-900 dark:text-slate-100">Returns:</span>
            <span className="ml-2 font-mono text-emerald-600 dark:text-emerald-400">{item.syntax.return.type}</span>
            {item.syntax.return.description && (
              <span className="ml-2 text-slate-600 dark:text-slate-300">{sanitizeDocText(item.syntax.return.description)}</span>
            )}
          </div>
        )}

        {remarks && (
          <div className="rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-900 dark:border-amber-700/60 dark:bg-amber-500/10 dark:text-amber-200">
            <span className="font-medium">Remarks:</span>
            <span className="ml-2">{remarks}</span>
          </div>
        )}
      </section>

      {memberGroups.length > 0 && (
        <section className="space-y-8">
          {memberGroups.map(([groupName, members]) => (
            <div key={groupName} className="space-y-4">
              <h2 className="text-lg font-semibold tracking-tight text-slate-900 dark:text-slate-100">{groupName}s</h2>
              <div className="space-y-4">
                {members.map(member => (
                  <MemberCard key={member.uid} member={member} />
                ))}
              </div>
            </div>
          ))}
        </section>
      )}
    </div>
  )
}
