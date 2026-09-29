// Utilities for cleaning and sanitizing documentation text

/**
 * Clean XML tags and xref from description text.
 * Handles DocFX-style xref tags and extracts readable content.
 */
export function sanitizeDocText(desc?: string): string | undefined {
  if (!desc) return undefined

  // Remove xref tags and extract the readable content
  let cleaned = desc.replace(/<xref\b([^>]*?)(?:\/>|>([\s\S]*?)<\/xref>)/gi, (_, attributes: string, text: string | undefined) => {
    const href = attributes.match(/\bhref=["']([^"']+)["']/i)?.[1] ?? ""
    const name = href.split(/[.#]/).at(-1)?.replace(/`\d+/g, "") ?? ""
    return text?.trim() || name
  })

  // Remove any remaining XML tags
  cleaned = cleaned.replace(/<\/?(?:p|div|br|li|ul|ol|pre)\b[^>]*>/gi, " ").replace(/<[^>]+>/g, "")
  const entities: Record<string, string> = { "&lt;": "<", "&gt;": ">", "&amp;": "&", "&quot;": '"', "&apos;": "'", "&nbsp;": " " }
  cleaned = cleaned
    .replace(/&(?:lt|gt|amp|quot|apos|nbsp);/g, (entity) => entities[entity])
    .replace(/&#(x[\da-f]+|\d+);/gi, (entity, code: string) => {
      const value = code[0].toLowerCase() === "x" ? parseInt(code.slice(1), 16) : Number(code)
      return value > 0 && value <= 0x10ffff ? String.fromCodePoint(value) : entity
    })
    .replace(/\[([^\]]+)\]\([^)]+\)/g, "$1")
    .replace(/`([^`]+)`/g, "$1")
    .replace(/\*\*([^*]+)\*\*/g, "$1")

  // Clean up extra whitespace
  cleaned = cleaned.replace(/\s+/g, " ").trim()

  return cleaned.length > 0 ? cleaned : undefined
}

export function apiDescription(item: { name: string; fullName?: string; type?: string; namespace?: string; summary?: string }) {
  const name = item.fullName ?? item.name
  const kind = item.type?.toLowerCase() ?? "type"
  const summary = sanitizeDocText(item.summary)
  return summary
    ? `${item.name}: ${summary}`
    : `API reference for the ${name} ${kind}${item.namespace && !name.startsWith(item.namespace) ? ` in ${item.namespace}` : ""}. Browse its declaration and documented members.`
}

/** Promote a document's existing title, preserving its slug and lower-level headings. */
export function ensurePageHeading(markdown: string, title: string): string {
  const lines = markdown.split(/\r?\n/)
  let fence: string | undefined
  const headings: number[] = []
  for (let index = 0; index < lines.length; index++) {
    const marker = lines[index].match(/^\s*(`{3,}|~{3,})/)?.[1]
    if (marker) {
      if (!fence) fence = marker
      else if (marker[0] === fence[0] && marker.length >= fence.length) fence = undefined
      continue
    }
    if (!fence && /^#{1,6}\s/.test(lines[index].trimStart())) headings.push(index)
  }
  if (headings.some((index) => /^\s*#\s/.test(lines[index]))) return markdown
  if (!headings.length) return `# ${title}\n\n${markdown}`
  lines[headings[0]] = lines[headings[0]].replace(/^\s*#{2,6}\s/, "# ")
  return lines.join("\n")
}
