import { generateComponents } from './components.generated'

export interface ComponentInfo {
  name: string
  description: string
  category: "Layout" | "Input" | "Display" | "Utilities"
  parameters?: Array<{
    name: string
    type: string
    default?: string
    description: string
  }>
  // paths to example files
  // the files are located in razor-console/src/RazorConsole.Website/Components/
  // e.g., ["Align_1.razor"]
  examples: string[]
}

// Auto-generated from XML documentation
export const components: ComponentInfo[] = generateComponents()
