import { useParams, Navigate } from "react-router-dom"
import { components } from "@/data/components"
import { ComponentPreview } from "@/components/components/ComponentPreview"
import { cn, getCategoryBadgeColor } from "@/lib/utils"
import ApiSection from "@/components/components/ApiSection"
import ParametersTable from "@/components/components/ParametersTable"

export default function ComponentDetail() {
  const { name } = useParams()
  const component = components.find((c) => c.name.toLowerCase() === name?.toLowerCase())

  if (!component) {
    return <Navigate to="/components" replace />
  }

  return (
    <div className="animate-in fade-in slide-in-from-bottom-4 space-y-8 duration-500">
      {/* Header */}
      <div>
        <div className="mb-3 flex flex-wrap items-center gap-3">
          <h1 className="text-3xl font-bold tracking-tight text-slate-900 dark:text-slate-50">
            {component.name}
          </h1>
          <span
            className={cn(
              "rounded-full border px-3 py-1 text-xs font-semibold tracking-widest uppercase",
              getCategoryBadgeColor(component.category)
            )}
          >
            {component.category}
          </span>
        </div>
        <p className="text-lg text-slate-600 dark:text-slate-300">{component.description}</p>
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
  )
}
