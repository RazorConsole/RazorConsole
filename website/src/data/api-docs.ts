import { load } from 'js-yaml'

export interface DocfxTocNode {
  uid?: string
  name: string
  type?: string
  items?: DocfxTocNode[]
}

export interface DocfxSyntaxParameter {
  id: string
  name?: string
  type?: string
  description?: string
}

export interface DocfxSyntaxReturn {
  type?: string
  description?: string
}

export interface DocfxSyntax {
  content?: string
  contentCs?: string
  parameters?: DocfxSyntaxParameter[]
  return?: DocfxSyntaxReturn
}

export interface DocfxApiMember {
  uid: string
  name: string
  nameWithType?: string
  fullName?: string
  type?: string
  summary?: string
  remarks?: string
  examples?: string[]
  syntax?: DocfxSyntax
  attributes?: DocfxAttribute[]
}

export interface DocfxApiItem extends DocfxApiMember {
  assemblies?: string[]
  namespace?: string
  children?: string[]
  members?: DocfxApiMember[]
}

export interface DocfxAttribute {
  type?: string
  constructor?: string
}

type RawYaml = Record<string, unknown> & {
  items?: unknown[]
  references?: unknown[]
}

const docfxRawFiles = import.meta.glob('../../.docfx/**/*.yml', {
  eager: true,
  query: '?raw',
  import: 'default',
}) as Record<string, string>

let cachedToc: DocfxTocNode[] | undefined
let cachedItems: Record<string, DocfxApiItem> | undefined

function normalizeString(value: unknown): string | undefined {
  if (typeof value !== 'string') {
    return undefined
  }
  const trimmed = value.trim()
  if (!trimmed) {
    return undefined
  }
  return trimmed.replace(/\r\n/g, '\n')
}

function parseYaml(raw: string): RawYaml | undefined {
  try {
    const result = load(raw)
    return result && typeof result === 'object' ? (result as RawYaml) : undefined
  } catch (error) {
    console.warn('Failed to parse DocFX YAML', error)
    return undefined
  }
}

function toSyntax(raw: unknown): DocfxSyntax | undefined {
  if (!raw || typeof raw !== 'object') {
    return undefined
  }

  const source = raw as Record<string, unknown>
  const parametersRaw = Array.isArray(source.parameters) ? source.parameters : undefined

  const parameters = parametersRaw
    ?.map(entry => {
      if (!entry || typeof entry !== 'object') {
        return undefined
      }
      const parameter = entry as Record<string, unknown>
      const id = normalizeString(parameter.id) ?? normalizeString(parameter.name)
      if (!id) {
        return undefined
      }

      const param: DocfxSyntaxParameter = {
        id,
        name: normalizeString(parameter.name) ?? id,
        type: normalizeString(parameter.type),
        description: normalizeString(parameter.description),
      }

      return param
    })
    .filter((param): param is DocfxSyntaxParameter => Boolean(param) && Boolean(param?.id))

  const returnRaw = source.return && typeof source.return === 'object' ? (source.return as Record<string, unknown>) : undefined
  const returnInfo: DocfxSyntaxReturn | undefined = returnRaw
    ? {
        type: normalizeString(returnRaw.type),
        description: normalizeString(returnRaw.description),
      }
    : undefined

  const syntax: DocfxSyntax = {}

  const content = normalizeString(source.content)
  if (content) {
    syntax.content = content
  }

  const contentCs = normalizeString(source['content.csharp'] ?? source['content.cs'])
  if (contentCs) {
    syntax.contentCs = contentCs
  }

  if (parameters && parameters.length > 0) {
    syntax.parameters = parameters
  }

  if (returnInfo && (returnInfo.type || returnInfo.description)) {
    syntax.return = returnInfo
  }

  return Object.keys(syntax).length > 0 ? syntax : undefined
}

function ensureName(value: unknown, fallback: string): string {
  const normalized = normalizeString(value)
  return normalized ?? fallback
}

function toAttributes(raw: unknown): DocfxAttribute[] | undefined {
  if (!Array.isArray(raw)) {
    return undefined
  }

  const attributes = raw
    .map(entry => {
      if (!entry || typeof entry !== 'object') {
        return undefined
      }

      const source = entry as Record<string, unknown>
      const type = normalizeString(source.type)
      const ctor = normalizeString(source.ctor)

      if (!type && !ctor) {
        return undefined
      }

      const attribute: DocfxAttribute = {
        type,
        constructor: ctor,
      }
      
      return attribute
    })
    .filter((attribute): attribute is DocfxAttribute => Boolean(attribute))

  return attributes.length > 0 ? attributes : undefined
}

function toMember(raw: unknown): DocfxApiMember | undefined {
  if (!raw || typeof raw !== 'object') {
    return undefined
  }

  const source = raw as Record<string, unknown>
  const uid = normalizeString(source.uid)
  if (!uid) {
    return undefined
  }

  const name = normalizeString(source.name) ?? normalizeString(source.id) ?? uid

  const member: DocfxApiMember = {
    uid,
    name,
  }

  const nameWithType = normalizeString(source.nameWithType)
  if (nameWithType) {
    member.nameWithType = nameWithType
  }

  const fullName = normalizeString(source.fullName)
  if (fullName) {
    member.fullName = fullName
  }

  const type = normalizeString(source.type)
  if (type) {
    member.type = type
  }

  const summary = normalizeString(source.summary)
  if (summary) {
    member.summary = summary
  }

  const remarks = normalizeString(source.remarks)
  if (remarks) {
    member.remarks = remarks
  }

  if (Array.isArray(source.examples)) {
    const examples = source.examples
      .map(example => normalizeString(example))
      .filter((line): line is string => Boolean(line))
    if (examples.length > 0) {
      member.examples = examples
    }
  }

  const syntax = toSyntax(source.syntax)
  if (syntax) {
    member.syntax = syntax
  }

  const attributes = toAttributes(source.attributes)
  if (attributes) {
    member.attributes = attributes
  }

  return member
}

function toReferenceMember(raw: unknown): DocfxApiMember | undefined {
  if (!raw || typeof raw !== 'object') {
    return undefined
  }

  const source = raw as Record<string, unknown>
  const uid = normalizeString(source.uid)
  if (!uid) {
    return undefined
  }

  const member: DocfxApiMember = {
    uid,
    name: ensureName(source.name, uid),
  }

  const nameWithType = normalizeString(source.nameWithType)
  if (nameWithType) {
    member.nameWithType = nameWithType
  }

  const fullName = normalizeString(source.fullName)
  if (fullName) {
    member.fullName = fullName
  }

  const type = normalizeString(source.type)
  if (type) {
    member.type = type
  }

  return member
}

function toTocNode(raw: unknown): DocfxTocNode | undefined {
  if (!raw || typeof raw !== 'object') {
    return undefined
  }

  const source = raw as Record<string, unknown>
  const name = ensureName(source.name, normalizeString(source.uid) ?? 'Untitled')
  const node: DocfxTocNode = { name }

  const uid = normalizeString(source.uid)
  if (uid) {
    node.uid = uid
  }

  const type = normalizeString(source.type)
  if (type) {
    node.type = type
  }

  if (Array.isArray(source.items)) {
    const items = source.items
      .map(entry => toTocNode(entry))
      .filter((entry): entry is DocfxTocNode => Boolean(entry))
    if (items.length > 0) {
      node.items = items
    }
  }

  return node
}

function createApiToc(): DocfxTocNode[] {
  if (cachedToc) {
    return cachedToc
  }

  const tocEntries = Object.entries(docfxRawFiles).filter(([filePath]) => filePath.endsWith('toc.yml'))

  if (tocEntries.length === 0) {
    cachedToc = []
    return cachedToc
  }

  const [, raw] = tocEntries[0]
  const parsed = parseYaml(raw)
  const items = parsed && Array.isArray(parsed.items) ? parsed.items : []

  cachedToc = items
    .map(entry => toTocNode(entry))
    .filter((entry): entry is DocfxTocNode => Boolean(entry))

  return cachedToc
}

function createApiItems(): Record<string, DocfxApiItem> {
  if (cachedItems) {
    return cachedItems
  }

  const entries = Object.entries(docfxRawFiles).filter(([filePath]) => !filePath.endsWith('toc.yml'))
  const items = new Map<string, DocfxApiItem>()

  for (const [, raw] of entries) {
    const parsed = parseYaml(raw)
    if (!parsed) {
      continue
    }

    const rawItems = Array.isArray(parsed.items) ? parsed.items : []
    if (rawItems.length === 0) {
      continue
    }

    const rootRaw = rawItems[0]
    const rootMember = toMember(rootRaw)
    if (!rootMember) {
      continue
    }

    const itemLookup = new Map<string, unknown>()
    for (const entry of rawItems) {
      if (entry && typeof entry === 'object' && typeof (entry as Record<string, unknown>).uid === 'string') {
        itemLookup.set((entry as Record<string, unknown>).uid as string, entry)
      }
    }

    const referencesRaw = Array.isArray(parsed.references) ? parsed.references : []

    const childUids = Array.isArray((rootRaw as Record<string, unknown>).children)
      ? ((rootRaw as Record<string, unknown>).children as unknown[]).filter((child): child is string => typeof child === 'string')
      : []

    const members = childUids
      .map(uid => {
        const child = itemLookup.get(uid)
        if (child) {
          return toMember(child)
        }
        const reference = referencesRaw.find(ref => ref && typeof ref === 'object' && (ref as Record<string, unknown>).uid === uid)
        return reference ? toReferenceMember(reference) : undefined
      })
      .filter((member): member is DocfxApiMember => Boolean(member))

    const assemblies = Array.isArray((rootRaw as Record<string, unknown>).assemblies)
      ? ((rootRaw as Record<string, unknown>).assemblies as unknown[]).filter((assembly): assembly is string => typeof assembly === 'string')
      : undefined

    const namespace = normalizeString((rootRaw as Record<string, unknown>).namespace)

    const apiItem: DocfxApiItem = {
      ...rootMember,
    }

    if (assemblies && assemblies.length > 0) {
      apiItem.assemblies = assemblies
    }

    if (namespace) {
      apiItem.namespace = namespace
    }

    if (childUids.length > 0) {
      apiItem.children = childUids
    }

    if (members.length > 0) {
      apiItem.members = members
    }

    items.set(apiItem.uid, apiItem)
  }

  cachedItems = Object.fromEntries(Array.from(items.entries()).sort(([a], [b]) => a.localeCompare(b)))

  return cachedItems
}

export const apiItems = createApiItems()
export const apiToc = createApiToc()