import type { ComponentInfo } from './components'
import { parseXmlDocumentation } from './xml-parser'

// Manual metadata that can't be extracted from XML
export const componentMetadata: Record<string, Partial<ComponentInfo>> = {
  Align: {
    category: "Layout",
    description: "Wraps child content in an alignment container.",
    examples: ["Align_1.razor"]
  },
  Border: {
    category: "Display",
    description: "Creates a bordered panel around its children.",
    examples: ["Border_1.razor"]
  },
  BarChart: {
    category: "Display",
    description: "Renders a horizontal bar chart with optional label, colors and value display.",
    examples: ["BarChart_1.razor"]
  },
  BreakdownChart: {
    category: "Display",
    description: "Displays a breakdown chart showing proportional data.",
    examples: ["BreakdownChart_1.razor"]
  },
  Columns: {
    category: "Layout",
    description: "Arranges children in columns.",
    examples: ["Columns_1.razor"]
  },
  Figlet: {
    category: "Display",
    description: "Renders ASCII art text.",
    examples: ["Figlet_1.razor"]
  },
  Grid: {
    category: "Layout",
    description: "Arranges children in a grid layout.",
    examples: ["Grid_1.razor"]
  },
  Markdown: {
    category: "Display",
    description: "Renders markdown content.",
    examples: ["Markdown_1.razor"]
  },
  Markup: {
    category: "Display",
    description: "Renders styled text with markup.",
    examples: ["Markup_1.razor"]
  },
  Padder: {
    category: "Layout",
    description: "Adds padding around its children.",
    examples: ["Padder_1.razor"]
  },
  Panel: {
    category: "Display",
    description: "Creates a bordered panel with optional title.",
    examples: ["Panel_1.razor"]
  },
  Rows: {
    category: "Layout",
    description: "Arranges children in rows.",
    examples: ["Rows_1.razor"]
  },
  Scrollable: {
    category: "Layout",
    description: "Provides scrollable content area.",
    examples: ["Scrollable_1.razor"]
  },
  Table: {
    category: "Display",
    description: "Renders a data table.",
    examples: ["Table_1.razor"]
  },
  Text: {
    category: "Display",
    description: "Renders plain text content.",
    examples: ["Text_1.razor"]
  },
  TextInput: {
    category: "Input",
    description: "Single-line text input field.",
    examples: ["TextInput_1.razor"]
  },
  Button: {
    category: "Input",
    description: "Interactive button component.",
    examples: ["Button_1.razor"]
  },
}

// Type overrides for better accuracy than inference
export const typeOverrides: Record<string, Record<string, string>> = {
  Align: {
    Horizontal: "HorizontalAlignment",
    Vertical: "VerticalAlignment",
    Width: "int?",
    Height: "int?",
  },
  Border: {
    BorderColor: "Color?",
    BoxBorder: "BoxBorder",
    Padding: "Padding",
  },
  BarChart: {
    BarChartItems: "List<IBarChartItem>",
    Width: "int?",
    Label: "string?",
    LabelForeground: "Color?",
    LabelBackground: "Color?",
    LabelDecoration: "Decoration?",
    LabelAlignment: "Justify?",
    MaxValue: "double?",
    ShowValues: "bool",
    Culture: "CultureInfo?",
  },
  Panel: {
    Title: "string?",
    TitleColor: "Color?",
    BorderColor: "Color?",
    Border: "BoxBorder",
    Height: "int?",
    Padding: "Padding?",
    Width: "int?",
    Expand: "bool",
  },
  Figlet: {
    Content: "string?",
    Justify: "Justify?",
    Color: "Color?",
  },
  Markup: {
    Content: "string?",
    Foreground: "Color?",
    Background: "Color?",
    Decoration: "Decoration?",
    link: "string?",
  },
  TextInput: {
    Value: "string",
    ValueChanged: "EventCallback<string>",
    Placeholder: "string?",
    IsPassword: "bool",
    MaxLength: "int?",
  },
}

/**
 * Generate components list by merging XML docs with manual metadata
 */
export function generateComponents(): ComponentInfo[] {
  const xmlDocs = parseXmlDocumentation()
  const components: ComponentInfo[] = []

  // Iterate through components that have metadata
  Object.keys(componentMetadata).forEach(componentName => {
    const metadata = componentMetadata[componentName]
    const xmlDoc = xmlDocs.get(componentName)

    // Get parameters from XML or fallback to empty array
    const parameters = xmlDoc?.parameters.map(param => {
      // Apply type override if available
      const type = typeOverrides[componentName]?.[param.name] || param.type

      return {
        name: param.name,
        type,
        description: param.description,
        default: undefined, // Could be extracted from source code analysis
      }
    }) || []

    components.push({
      name: componentName,
      description: metadata.description || xmlDoc?.summary || `${componentName} component`,
      category: metadata.category || "Utilities",
      examples: metadata.examples || [],
      parameters: parameters.length > 0 ? parameters : undefined,
    })
  })

  return components
}
