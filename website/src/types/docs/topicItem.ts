export interface Heading {
  level: number
  title: string
  id: string
}

export interface TopicItem {
  id: string
  title: string
  content: string
  filePath: string
  headings: Heading[]
}
