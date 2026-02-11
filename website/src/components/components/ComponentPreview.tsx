import CodeBlock from "@/components/ui/CodeBlock"
import XTermPreview from "@/components/components/XTermPreview"
import type { ComponentInfo } from "@/types/components/componentInfo"

const examples = import.meta.glob("../../../../src/RazorConsole.Website/Components/*.razor", {
  query: "?raw",
  import: "default",
  eager: true,
}) as Record<string, string>

export function ComponentPreview({ component }: { component: ComponentInfo }) {
  const exampleFilename = component.examples[0]
  const examplePath = `../../../../src/RazorConsole.Website/Components/${exampleFilename}`
  const code = examples[examplePath] || `Example not found: ${examplePath}`

  return (
    <div className="group relative my-4 flex flex-col space-y-4">
      <XTermPreview elementId={component.name} className={`h-[300px]`} style={{ height: component.previewHeight }} />

      <div className="flex flex-col space-y-4">
        <div className="w-full [&_pre]:my-0 [&_pre]:max-h-[300px] [&_pre]:overflow-auto">
          <CodeBlock code={code} language="razor" showCopy={true} />
        </div>
      </div>
    </div>
  )
}
