import type { ComponentInfo } from "@/types/components/componentInfo"
import { apiItems, type DocfxApiItem, type DocfxApiMember } from "./api-docs"

// Manual metadata that can't be extracted from XML
export const componentMetadata: Record<string, Partial<ComponentInfo>> = {
  Align: {
    category: "Layout",
    description: "Wraps child content in an alignment container.",
    examples: ["Align_1.razor"],
  },
  Border: {
    category: "Display",
    description: "Creates a bordered panel around its children.",
    examples: ["Border_1.razor"],
  },
  BarChart: {
    category: "Display",
    description: "Renders a horizontal bar chart with optional label, colors and value display.",
    examples: ["BarChart_1.razor"],
  },
  BreakdownChart: {
    category: "Display",
    description: "Displays a breakdown chart showing proportional data.",
    examples: ["BreakdownChart_1.razor"],
  },
  Columns: {
    category: "Layout",
    description: "Arranges children in columns.",
    examples: ["Columns_1.razor"],
  },
  Figlet: {
    category: "Display",
    description: "Renders ASCII art text.",
    examples: ["Figlet_1.razor"],
  },
  Grid: {
    category: "Layout",
    description: "Arranges children in a grid layout.",
    examples: ["Grid_1.razor"],
  },
  ModalWindow:{
    category: "Display",
    description: "Renders a modal in a dialog.",
    examples: ["ModalWindow_1.razor"],
  },
  Markdown: {
    category: "Display",
    description: "Renders markdown content.",
    examples: ["Markdown_1.razor"],
  },
  Markup: {
    category: "Display",
    description: "Renders styled text with markup.",
    examples: ["Markup_1.razor"],
  },
  Padder: {
    category: "Layout",
    description: "Adds padding around its children.",
    examples: ["Padder_1.razor"],
  },
  Panel: {
    category: "Display",
    description: "Creates a bordered panel with optional title.",
    examples: ["Panel_1.razor"],
  },
  Rows: {
    category: "Layout",
    description: "Arranges children in rows.",
    examples: ["Rows_1.razor"],
  },
  Scrollable: {
    category: "Layout",
    description: "Provides scrollable content area.",
    examples: ["Scrollable_1.razor"],
  },
  Select: {
    category: "Input",
    description: "Interactive dropdown for choosing a value with keyboard navigation.",
    examples: ["Select_1.razor"],
  },
  StepChart: {
    category: "Display",
    description:
      "Renders a terminal step chart using Unicode box-drawing characters. Perfect for displaying discrete value changes over time or categories.",
    examples: ["StepChart_1.razor"],
  },
  SyntaxHighlighter: {
    category: "Display",
    description: "Renders highlighted code blocks with SyntaxHighlightingService.",
    examples: ["SyntaxHighlighter_1.razor"],
  },
  Spinner: {
    category: "Utilities",
    description: "Shows a Spectre spinner with optional message.",
    examples: ["Spinner_1.razor"],
  },
  SpectreCanvas: {
    category: "Display",
    description: "Renders an array of pixels with different colors.",
    examples: ["SpectreCanvas_1.razor"],
  },
  Newline: {
    category: "Utilities",
    description: "Emits a single line break. No parameters.",
    examples: ["Newline_1.razor"],
  },
  Table: {
    category: "Display",
    description: "Renders a data table.",
    examples: ["Table_1.razor"],
  },
  TextInput: {
    category: "Input",
    description: "Single-line text input field.",
    examples: ["TextInput_1.razor"],
  },
  TextButton: {
    category: "Input",
    description: "Interactive button component.",
    examples: ["TextButton_1.razor"],
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

const docfxNameOverrides: Record<string, string> = {
  Button: "TextButton",
  Table: "SpectreTable",
}

function resolveDocfxItemName(componentName: string): string[] {
  const overrideName = docfxNameOverrides[componentName] ?? componentName
  const base = `RazorConsole.Components.${overrideName}`
  return [base, `${base}\`1`, `${base}\`2`]
}

function isParameterMember(member: Pick<DocfxApiMember, "type" | "attributes">): boolean {
  if (member.type !== "Property") {
    return false
  }

  const attributes = member.attributes ?? []
  return attributes.some((attr) => {
    if (!attr.type) {
      return false
    }
    return (
      attr.type.endsWith(".ParameterAttribute") ||
      attr.type.endsWith(".CascadingParameterAttribute")
    )
  })
}

function extractParameters(componentName: string, docfxItem: DocfxApiItem | undefined) {
  if (!docfxItem?.members) {
    return undefined
  }

  const parameters = docfxItem.members
    .filter(isParameterMember)
    .map((member) => {
      const paramName = member.name
      const overrideType = typeOverrides[componentName]?.[paramName]
      const inferredType = member.syntax?.return?.type ?? member.syntax?.content ?? "object"

      return {
        name: paramName,
        type: overrideType ?? inferredType,
        description: member.summary ?? "",
      }
    })
    .filter((param) => param.name)

  return parameters.length > 0 ? parameters : undefined
}

export function generateComponents(): ComponentInfo[] {
  const components: ComponentInfo[] = []

  // Iterate through components that have metadata
  Object.keys(componentMetadata).forEach((componentName) => {
    const metadata = componentMetadata[componentName]

    const candidateItem = resolveDocfxItemName(componentName)
      .map((candidate) => apiItems[candidate])
      .find((item) => item != null)

    const overrideName = docfxNameOverrides[componentName] ?? componentName

    const docfxItem =
      candidateItem ??
      Object.values(apiItems).find((item) => {
        if (!item.namespace?.startsWith("RazorConsole.Components")) {
          return false
        }

        const simpleName = item.name.replace(/`\d+$/, "")
        return simpleName === overrideName
      })

    const parameters = extractParameters(componentName, docfxItem)

    components.push({
      name: componentName,
      description: metadata.description || docfxItem?.summary || `${componentName} component`,
      category: metadata.category || "Utilities",
      examples: metadata.examples || [],
      parameters,
    })
  })

  return components
}
