import { useState, useMemo, Fragment } from "react";
import { useParams, Navigate, Link } from "react-router-dom";
import { components } from "@/data/components";
import { ComponentPreview } from "@/components/ComponentPreview";
import { TypeLink } from "@/components/TypeLink";
import {
    Search,
    Settings,
    ExternalLink,
    Code2,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { type MemberCategory, getCategoryIcon, categorizeMember, CATEGORY_ORDER } from "@/lib/categories";
import { sanitizeDocText } from "@/lib/doc-utils";

interface Parameter {
    name: string;
    type: string;
    default?: string;
    description: string;
}

// Get category badge color
function getCategoryBadgeColor(category: string) {
    switch (category) {
        case "Layout":
            return "border-blue-200 bg-blue-50 text-blue-700 dark:border-blue-500/40 dark:bg-blue-500/10 dark:text-blue-300";
        case "Input":
            return "border-purple-200 bg-purple-50 text-purple-700 dark:border-purple-500/40 dark:bg-purple-500/10 dark:text-purple-300";
        case "Display":
            return "border-teal-200 bg-teal-50 text-teal-700 dark:border-teal-500/40 dark:bg-teal-500/10 dark:text-teal-300";
        case "Utilities":
            return "border-orange-200 bg-orange-50 text-orange-700 dark:border-orange-500/40 dark:bg-orange-500/10 dark:text-orange-300";
        default:
            return "border-slate-200 bg-slate-50 text-slate-700 dark:border-slate-500/40 dark:bg-slate-500/10 dark:text-slate-300";
    }
}

// API Section Component
function ApiSection({ componentName }: { componentName: string }) {
    return (
        <section className="space-y-3">
            <div className="flex items-center gap-2">
                <span className="text-blue-600 dark:text-blue-400">
                    <Code2 className="h-5 w-5" />
                </span>
                <h3 className="text-xl font-semibold text-slate-900 dark:text-slate-100">
                    API
                </h3>
            </div>
            <div className="flex items-center gap-3">
                <Link
                    to={`/api/RazorConsole.Components.${componentName}`}
                    className="inline-flex items-center gap-1.5 rounded-md bg-blue-50 px-1.5 text-sm font-medium text-blue-600 transition-colors hover:bg-blue-100 dark:bg-blue-500/10 dark:text-blue-400 dark:hover:bg-blue-500/20"
                >
                    <code className="font-mono text-sm font-medium text-slate-900 dark:text-slate-100">
                        &lt;{componentName}&gt;
                    </code>
                    <ExternalLink className="h-3.5 w-3.5" />
                </Link>
            </div>
        </section>
    );
}

// Parameters Table Component
function ParametersTable({ parameters }: { parameters: Parameter[] }) {
    const [searchQuery, setSearchQuery] = useState("");

    const filteredParams = useMemo(() => {
        if (!searchQuery.trim()) return parameters;
        const query = searchQuery.toLowerCase();
        return parameters.filter(
            (p) =>
                p.name.toLowerCase().includes(query) ||
                p.type.toLowerCase().includes(query) ||
                (p.description ?? "").toLowerCase().includes(query)
        );
    }, [parameters, searchQuery]);

    const groupedParams = useMemo(() => {
        const groups = new Map<MemberCategory, Parameter[]>();
        for (const param of filteredParams) {
            const category = categorizeMember(param.name, param.type, param.description);
            if (!groups.has(category)) {
                groups.set(category, []);
            }
            groups.get(category)?.push(param);
        }

        return CATEGORY_ORDER
            .filter((cat) => groups.has(cat))
            .map((cat) => ({ category: cat, params: groups.get(cat) ?? [] }));
    }, [filteredParams]);

    return (
        <section className="space-y-4">
            <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
                <div className="flex items-center gap-2">
                    <span className="text-blue-600 dark:text-blue-400">
                        <Settings className="h-5 w-5" />
                    </span>
                    <h3 className="text-xl font-semibold text-slate-900 dark:text-slate-100">
                        Parameters
                    </h3>
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
                                        <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400">
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
                                                <span className="text-slate-400">
                                                    —
                                                </span>
                                            )}
                                        </td>
                                        <td className="px-4 py-3 text-slate-600 dark:text-slate-300">
                                            {sanitizeDocText(
                                                param.description
                                            ) ?? "—"}
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
    );
}

export default function ComponentDetail() {
    const { name } = useParams();
    const component = components.find(
        (c) => c.name.toLowerCase() === name?.toLowerCase()
    );

    if (!component) {
        return <Navigate to="/components" replace />;
    }

    return (
        <div className="space-y-8 animate-in fade-in slide-in-from-bottom-4 duration-500">
            {/* Header */}
            <div>
                <div className="flex flex-wrap items-center gap-3 mb-3">
                    <h1 className="text-3xl font-bold tracking-tight text-slate-900 dark:text-slate-50">
                        {component.name}
                    </h1>
                    <span
                        className={cn(
                            "rounded-full border px-3 py-1 text-xs font-semibold uppercase tracking-widest",
                            getCategoryBadgeColor(component.category)
                        )}
                    >
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
                <ParametersTable parameters={component.parameters} />
            )}

             {/* API */}
            <ApiSection componentName={component.name} />
        </div>
    );
}
