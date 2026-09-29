import { generateComponents } from "./components.generated"
import type { ComponentInfo } from "@/types/components/componentInfo"

// Auto-generated from DocFX metadata
export const components: ComponentInfo[] = generateComponents()

export function componentPathForApi(uid: string): string | undefined {
  const component = components.find((entry) => `RazorConsole.Components.${entry.apiId}` === uid)
  return component ? `/components/${component.name.toLowerCase()}/` : undefined
}
