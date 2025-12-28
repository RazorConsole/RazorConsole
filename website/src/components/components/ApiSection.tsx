import { Code2, ExternalLink } from "lucide-react"
import { Link } from "react-router-dom"

export default function ApiSection({ componentName }: { componentName: string }) {
  return (
    <section className="space-y-3">
      <div className="flex items-center gap-2">
        <span className="text-blue-600 dark:text-blue-400">
          <Code2 className="h-5 w-5" />
        </span>
        <h3 className="text-xl font-semibold text-slate-900 dark:text-slate-100">API</h3>
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
  )
}
