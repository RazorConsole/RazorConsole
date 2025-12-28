import { Link } from "react-router-dom"
import { components } from "@/data/components"
import { ArrowRight, Box } from "lucide-react"
import { cn, getCategoryBadgeColor } from "@/lib/utils"

export default function ComponentsOverview() {
  return (
    <div className="animate-in fade-in slide-in-from-bottom-4 space-y-10 duration-500">
      <div className="space-y-2">
        <h1 className="bg-gradient-to-r from-slate-900 to-slate-700 bg-clip-text text-4xl font-bold tracking-tight text-transparent dark:from-slate-100 dark:to-slate-400">
          Built-in Components
        </h1>
        <p className="max-w-2xl text-lg text-slate-600 dark:text-slate-400">
          RazorConsole ships with 20+ ready-to-use components that wrap Spectre.Console constructs,
          designed to build beautiful TUI applications effortlessly.
        </p>
      </div>

      <div className="grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-3">
        {components.map((component) => (
          <Link
            key={component.name}
            to={`/components/${component.name.toLowerCase()}`}
            className={cn(
              "group relative flex flex-col justify-between overflow-hidden rounded-xl border border-slate-200 bg-white p-6 shadow-sm transition-all duration-300",
              "hover:-translate-y-1 hover:border-blue-500/50 hover:shadow-md",
              "dark:border-slate-800 dark:bg-slate-950/50 dark:hover:border-blue-400/50 dark:hover:shadow-slate-900/50"
            )}
          >
            {/* Background Glow Effect on Hover */}
            <div className="absolute inset-0 -z-10 bg-gradient-to-br from-blue-50/50 via-transparent to-transparent opacity-0 transition-opacity duration-500 group-hover:opacity-100 dark:from-blue-950/10" />

            <div>
              <div className="mb-4 flex items-center justify-between">
                <span
                  className={cn(
                    "inline-flex items-center gap-1.5 rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-slate-600 dark:bg-slate-800 dark:text-slate-300",
                    getCategoryBadgeColor(component.category)
                  )}
                >
                  {component.category}
                </span>
                <Box className="h-4 w-4 text-slate-400 transition-colors group-hover:text-blue-500 dark:text-slate-600 dark:group-hover:text-blue-400" />
              </div>

              <h3 className="mb-2 font-mono text-lg font-bold text-slate-900 transition-colors group-hover:text-blue-600 dark:text-slate-100 dark:group-hover:text-blue-400">
                {component.name}
              </h3>

              <p className="line-clamp-2 text-sm leading-relaxed text-slate-500 dark:text-slate-400">
                {component.description}
              </p>
            </div>

            <div className="mt-6 flex items-center text-sm font-medium text-blue-600 opacity-0 transition-all duration-300 group-hover:translate-x-1 group-hover:opacity-100 dark:text-blue-400">
              View Details
              <ArrowRight className="ml-1.5 h-4 w-4" />
            </div>
          </Link>
        ))}
      </div>
    </div>
  )
}
