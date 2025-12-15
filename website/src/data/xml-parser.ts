import xmlDoc from '../../../artifacts/bin/RazorConsole.Core/debug_net10.0/RazorConsole.Core.xml?raw'

interface XmlParameter {
  name: string
  type: string
  description: string
  remarks?: string
  example?: string
  default?: string
}

interface XmlComponentDoc {
  name: string
  parameters: XmlParameter[]
  summary?: string
  remarks?: string
}

/**
 * Parse XML documentation and extract component parameters
 */
export function parseXmlDocumentation(): Map<string, XmlComponentDoc> {
  const parser = new DOMParser()
  const xmlDocument = parser.parseFromString(xmlDoc, 'text/xml')
  
  const components = new Map<string, XmlComponentDoc>()
  
  // Get all member elements
  const members = xmlDocument.querySelectorAll('member')
  
  members.forEach(member => {
    const memberName = member.getAttribute('name')
    if (!memberName) return
    
    // Match pattern: P:RazorConsole.Components.{ComponentName}.{PropertyName}
    const propertyMatch = memberName.match(/^P:RazorConsole\.Components\.(\w+)\.(\w+)$/)
    
    if (propertyMatch) {
      const [, componentName, propertyName] = propertyMatch
      
      // Get or create component entry
      if (!components.has(componentName)) {
        components.set(componentName, {
          name: componentName,
          parameters: [],
        })
      }
      
      const component = components.get(componentName)!
      
      // Extract description from summary tag with nested XML processing
      const summaryElement = member.querySelector('summary')
      const description = summaryElement ? processXmlContent(summaryElement) : ''
      
      // Extract remarks if available
      const remarksElement = member.querySelector('remarks')
      const remarks = remarksElement ? processXmlContent(remarksElement) : undefined
      
      // Extract example if available
      const exampleElement = member.querySelector('example')
      const example = exampleElement ? processXmlContent(exampleElement) : undefined
      
      // Extract default value from remarks or other tags
      const defaultValue = extractDefaultValue(member)
      
      // Try to infer type from member name or extract from XML
      // Note: XML docs don't include type info, we'll need to parse from the member name pattern
      // or maintain a manual type mapping
      const type = inferTypeFromProperty(componentName, propertyName)
      
      component.parameters.push({
        name: propertyName,
        type,
        description,
        remarks,
        example,
        default: defaultValue,
      })
    }
  })
  
  return components
}

/**
 * Process XML content and convert nested tags to readable text
 * Handles: <see cref="..." />, <see href="..." />, <c>code</c>, <para>, <code>, etc.
 */
function processXmlContent(element: Element): string {
  let text = ''
  
  element.childNodes.forEach(node => {
    if (node.nodeType === Node.TEXT_NODE) {
      // Plain text content
      text += node.textContent
    } else if (node.nodeType === Node.ELEMENT_NODE) {
      const el = node as Element
      const tagName = el.tagName.toLowerCase()
      
      switch (tagName) {
        case 'see':
          // Handle <see cref="..." /> and <see href="..." />
          const cref = el.getAttribute('cref')
          const href = el.getAttribute('href')
          
          if (href) {
            // External link: extract link text or URL
            const linkText = el.textContent?.trim() || href
            text += `[${linkText}](${href})`
          } else if (cref) {
            // Code reference: extract the simple name
            const simpleName = cref.split('.').pop()?.replace(/^[TPFME]:/, '') || cref
            text += `\`${simpleName}\``
          }
          break
          
        case 'c':
          // Inline code: <c>code</c>
          text += `\`${el.textContent?.trim() || ''}\``
          break
          
        case 'code':
          // Code block: <code>...</code>
          text += `\n\`\`\`\n${el.textContent?.trim() || ''}\n\`\`\`\n`
          break
          
        case 'para':
          // Paragraph: add spacing
          text += '\n\n' + processXmlContent(el) + '\n\n'
          break
          
        case 'paramref':
          // Parameter reference: <paramref name="..." />
          const paramName = el.getAttribute('name')
          text += paramName ? `\`${paramName}\`` : ''
          break
          
        case 'typeparamref':
          // Type parameter reference: <typeparamref name="..." />
          const typeParamName = el.getAttribute('name')
          text += typeParamName ? `\`${typeParamName}\`` : ''
          break
          
        case 'example':
          // Example: usually contains <code>
          text += '\n\nExample:' + processXmlContent(el)
          break
          
        case 'remarks':
          // Remarks: additional details
          text += '\n\n' + processXmlContent(el)
          break
          
        default:
          // For other tags, recursively process content
          text += processXmlContent(el)
      }
    }
  })
  
  // Clean up excessive whitespace
  return text
    .replace(/\n{3,}/g, '\n\n')  // Max 2 consecutive newlines
    .replace(/[ \t]+/g, ' ')      // Multiple spaces to single space
    .trim()
}

/**
 * Extract default value from member documentation
 * Looks for patterns like "Default is X" or "Default: X"
 */
function extractDefaultValue(member: Element): string | undefined {
  const summaryText = member.querySelector('summary')?.textContent || ''
  const remarksText = member.querySelector('remarks')?.textContent || ''
  const fullText = summaryText + ' ' + remarksText
  
  // Match patterns like "Default is X" or "Default: X"
  const defaultMatch = fullText.match(/Default\s+(?:is|:)\s+([^.]+)/i)
  if (defaultMatch) {
    return defaultMatch[1].trim()
  }
  
  // Match "If null" or "If <c>null</c>" patterns (implies nullable/optional)
  if (/If\s+(?:<c>)?null(?:<\/c>)?/i.test(fullText)) {
    return 'null'
  }
  
  return undefined
}

/**
 * Infer type from component and property name
 * This is a fallback - ideally types would be in XML or extracted from source
 */
function inferTypeFromProperty(componentName: string, propertyName: string): string {
  // Common patterns
  if (propertyName === 'ChildContent') return 'RenderFragment?'
  if (propertyName.includes('Color')) return 'Color?'
  if (propertyName.includes('Width') || propertyName.includes('Height')) return 'int?'
  if (propertyName.includes('Expand')) return 'bool'
  if (propertyName.endsWith('Items') || propertyName.includes('List')) return 'List<T>'
  if (propertyName.includes('Alignment')) return 'Alignment'
  if (propertyName.endsWith('Style')) return 'Style'
  
  // Default
  return 'string?'
}

/**
 * Get documentation for a specific component
 */
export function getComponentDocumentation(componentName: string): XmlComponentDoc | undefined {
  const docs = parseXmlDocumentation()
  return docs.get(componentName)
}

/**
 * Get all component names from XML docs
 */
export function getAvailableComponents(): string[] {
  const docs = parseXmlDocumentation()
  return Array.from(docs.keys())
}
