import type { Category } from "./category"

export interface ComponentInfo {
  name: string
  description: string
  category: Category
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
