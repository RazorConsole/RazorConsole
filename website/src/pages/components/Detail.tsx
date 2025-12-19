import { useState, useMemo, Fragment } from "react"
import { useParams, Navigate, Link } from "react-router-dom"
import { components, type ComponentInfo } from "@/data/components"
import { ComponentPreview } from "@/components/ComponentPreview"
import { Search, Settings, Zap, Box, FileCode, ExternalLink, BookOpen } from "lucide-react"
import { cn } from "@/lib/utils"

// Category definitions for parameters
type ParamCategory = 'Behavior' | 'Appearance' | 'Events' | 'Common' | 'Other'

interface Parameter {
    name: string
    type: string
    default?: string
    description: string
}

// Clean XML tags and xref from description
function cleanDescription(desc: string): string {
    if (!desc) return ''
    // Remove xref tags and extract the readable content
    let cleaned = desc.replace(/<xref[^>]*href="([^"]*)"[^>]*>([^<]*)<\/xref>/g, (_, href, text) => {
        // Extract just the type name from the href
        const parts = href.split('.')
        return parts[parts.length - 1] || text || ''
    })
    // Remove any remaining XML tags
    cleaned = cleaned.replace(/<[^>]+>/g, '')
    // Clean up extra whitespace
    cleaned = cleaned.replace(/\s+/g, ' ').trim()
    return cleaned
}

// Categorize a parameter based on its name and description
function categorizeParam(param: Parameter): ParamCategory {
    const name = param.name.toLowerCase()
    const desc = (param.description ?? '').toLowerCase()

    // Events
    if (name.startsWith('on') || name.includes('callback') || name.includes('event') ||
        param.type.includes('EventCallback')) {
        return 'Events'
    }

    // Appearance
    if (name.includes('color') || name.includes('style') || name.includes('class') ||
        name.includes('width') || name.includes('height') || name.includes('size') ||
        name.includes('border') || name.includes('background') || name.includes('foreground') ||
        desc.includes('appearance') || desc.includes('visual') || desc.includes('style')) {
        return 'Appearance'
    }

    // Behavior
    if (name.includes('enabled') || name.includes('disabled') || name.includes('readonly') ||
        name.includes('visible') || name.includes('focus') || name.includes('selected') ||
        desc.includes('behavior') || desc.includes('interact')) {
        return 'Behavior'
    }

    // Common
    if (name === 'childcontent' || name === 'id' || name === 'class' || name === 'style' ||
        name.includes('content') || name.includes('value') || name.includes('text') ||
        name === 'title' || name === 'label' || name === 'header') {
        return 'Common'
    }

    return 'Other'
}

// Get category icon
function getCategoryIcon(category: ParamCategory) {
    switch (category) {
        case 'Behavior': return <Settings className="h-4 w-4" />
        case 'Appearance': return <Box className="h-4 w-4" />
        case 'Events': return <Zap className="h-4 w-4" />
        case 'Common': return <FileCode className="h-4 w-4" />
        default: return null
    }
}

// Render type with links
function TypeLink({ type }: { type: string }) {
    if (!type) return <span className="text-slate-400">—</span>

    // Microsoft types
    if (type.startsWith('Microsoft.') || type.startsWith('System.')) {
        const shortName = type.split('.').pop() ?? type
        const docUrl = `https://learn.microsoft.com/dotnet/api/${type.replace(/`\d+/g, '-')}`
        return (
            <a
                href={docUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1 font-mono text-xs text-blue-600 hover:text-blue-700 hover:underline dark:text-blue-400 dark:hover:text-blue-300"
            >
                {shortName}
                <ExternalLink className="h-3 w-3" />
            </a>
        )
    }

    // Spectre.Console types
    if (type.startsWith('Spectre.')) {
        const shortName = type.split('.').pop() ?? type
        return (
            <a
                href="https://spectreconsole.net/"
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1 font-mono text-xs text-emerald-600 hover:text-emerald-700 hover:underline dark:text-emerald-400 dark:hover:text-emerald-300"
            >
                {shortName}
                <ExternalLink className="h-3 w-3" />
            </a>
        )
    }

    // Default
    return <code className="font-mono text-xs text-violet-600 dark:text-violet-400">{type}</code>
}

// Get category badge color
function getCategoryBadgeColor(category: string) {
    switch (category) {
        case 'Layout':
            return 'border-blue-200 bg-blue-50 text-blue-700 dark:border-blue-500/40 dark:bg-blue-500/10 dark:text-blue-300'
        case 'Input':
            return 'border-purple-200 bg-purple-50 text-purple-700 dark:border-purple-500/40 dark:bg-purple-500/10 dark:text-purple-300'
        case 'Display':
            return 'border-teal-200 bg-teal-50 text-teal-700 dark:border-teal-500/40 dark:bg-teal-500/10 dark:text-teal-300'
        case 'Utilities':
            return 'border-orange-200 bg-orange-50 text-orange-700 dark:border-orange-500/40 dark:bg-orange-500/10 dark:text-orange-300'
        default:
            return 'border-slate-200 bg-slate-50 text-slate-700 dark:border-slate-500/40 dark:bg-slate-500/10 dark:text-slate-300'
    }
}

// Parameters Table Component
function ParametersTable({ parameters, componentName }: { parameters: Parameter[], componentName: string }) {
    const [searchQuery, setSearchQuery] = useState('')

    const filteredParams = useMemo(() => {
        if (!searchQuery.trim()) return parameters
        const query = searchQuery.toLowerCase()
        return parameters.filter(p =>
            p.name.toLowerCase().includes(query) ||
            p.type.toLowerCase().includes(query) ||
            (p.description ?? '').toLowerCase().includes(query)
        )
    }, [parameters, searchQuery])

    const groupedParams = useMemo(() => {
        const groups = new Map<ParamCategory, Parameter[]>()
        for (const param of filteredParams) {
            const category = categorizeParam(param)
            if (!groups.has(category)) {
                groups.set(category, [])
            }
            groups.get(category)?.push(param)
        }

        const order: ParamCategory[] = ['Behavior', 'Appearance', 'Events', 'Common', 'Other']
        return order
            .filter(cat => groups.has(cat))
            .map(cat => ({ category: cat, params: groups.get(cat) ?? [] }))
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
                        <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" />
                        <input
                            type="search"
                            className="w-full rounded-lg border border-slate-200 bg-white py-2 pl-9 pr-3 text-sm text-slate-700 shadow-sm outline-none transition focus:border-blue-500 focus:ring-2 focus:ring-blue-200 dark:border-slate-700 dark:bg-slate-900 dark:text-slate-200 dark:focus:border-blue-400 dark:focus:ring-blue-500/40 sm:w-64"
                            placeholder="Search parameters..."
                            value={searchQuery}
                            onChange={e => setSearchQuery(e.target.value)}
                        />
                    </div>
                )}
            </div>

            <div className="overflow-hidden rounded-xl border border-slate-200 bg-white shadow-sm dark:border-slate-800 dark:bg-slate-900">
                <table className="min-w-full divide-y divide-slate-200 text-sm dark:divide-slate-800">
                    <thead className="bg-slate-50 text-left font-medium text-slate-600 dark:bg-slate-950 dark:text-slate-400">
                        <tr>
                            <th scope="col" className="px-4 py-3 font-semibold">Name</th>
                            <th scope="col" className="px-4 py-3 font-semibold">Type</th>
                            <th scope="col" className="px-4 py-3 font-semibold">Default</th>
                            <th scope="col" className="px-4 py-3 font-semibold">Description</th>
                        </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
                        {groupedParams.map(({ category, params }) => (
                            <Fragment key={category}>
                                {/* Category header row */}
                                <tr className="bg-slate-50/50 dark:bg-slate-800/30">
                                    <td colSpan={4} className="px-4 py-2">
                                        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
                                            {getCategoryIcon(category)}
                                            {category}
                                        </div>
                                    </td>
                                </tr>
                                {/* Parameter rows */}
                                {params.map((param, idx) => (
                                    <tr key={`${category}-${idx}`} className="group align-top hover:bg-slate-50 dark:hover:bg-slate-800/50">
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
                                                <code className="font-mono text-xs text-slate-600 dark:text-slate-400">{param.default}</code>
                                            ) : (
                                                <span className="text-slate-400">—</span>
                                            )}
                                        </td>
                                        <td className="px-4 py-3 text-slate-600 dark:text-slate-300">
                                            {cleanDescription(param.description)}
                                        </td>
                                    </tr>
                                ))}
                            </Fragment>
                        ))}
                        {filteredParams.length === 0 && (
                            <tr>
                                <td colSpan={4} className="px-4 py-8 text-center text-slate-500 dark:text-slate-400">
                                    No parameters found matching "{searchQuery}"
                                </td>
                            </tr>
                        )}
                    </tbody>
                </table>
            </div>

            {/* Link to API Reference */}
            <div className="flex items-start gap-3 rounded-lg border border-slate-200 bg-slate-50 p-4 dark:border-slate-700 dark:bg-slate-800/50">
                <BookOpen className="mt-0.5 h-5 w-5 shrink-0 text-slate-500 dark:text-slate-400" />
                <div className="space-y-1">
                    <p className="text-sm text-slate-700 dark:text-slate-300">
                        For complete API documentation including methods and events, see the API reference.
                    </p>
                    <Link
                        to={`/api/RazorConsole.Components.${componentName}`}
                        className="inline-flex items-center gap-1 text-sm font-medium text-blue-600 hover:text-blue-700 hover:underline dark:text-blue-400 dark:hover:text-blue-300"
                    >
                        View {componentName} API Reference
                        <ExternalLink className="h-3.5 w-3.5" />
                    </Link>
                </div>
            </div>
        </section>
    )
}

export default function ComponentDetail() {
    const { name } = useParams()
    const component = components.find(c => c.name.toLowerCase() === name?.toLowerCase())

    if (!component) {
        return <Navigate to="/components" replace />
    }

    return (
        <div className="space-y-8 animate-in fade-in slide-in-from-bottom-4 duration-500">
            {/* Header */}
            <div>
                <div className="flex flex-wrap items-center gap-3 mb-3">
                    <h1 className="text-3xl font-bold tracking-tight text-slate-900 dark:text-slate-50">
                        {component.name}
                    </h1>
                    <span className={cn(
                        'rounded-full border px-3 py-1 text-xs font-semibold uppercase tracking-widest',
                        getCategoryBadgeColor(component.category)
                    )}>
                        {component.category}
                    </span>
                </div>
                <p className="text-lg text-slate-600 dark:text-slate-300">
                    {component.description}
                </p>
            </div>

            {/* Preview */}
            <ComponentPreview component={component} />

            {/* Parameters */}
            {component.parameters && component.parameters.length > 0 && (
                <ParametersTable 
                    parameters={component.parameters} 
                    componentName={component.name}
                />
            )}
        </div>
    )
}
