export interface ShowcaseProject {
  slug?: string
  name: string
  description: string
  github?: string
  website?: string
  downloadUrl?: string
  videoUrl?: string
  installCommands?: Array<{
    label: string
    command: string
    nightlyCommand?: string
  }>
  imageUrls?: string[]
}

export const showcaseProjects: ShowcaseProject[] = [
  // Add your project here! Submit a PR to be featured.
  {
    name: "Waves",
    description: "GitHub Game Off 2025 entry - A console game built with RazorConsole.",
    github: "Skuzzle-UK/Waves",
    imageUrls: [
      "https://raw.githubusercontent.com/Skuzzle-UK/Waves/main/coverimage.png",
      "https://raw.githubusercontent.com/Skuzzle-UK/Waves/main/screenshot.png",
      "https://raw.githubusercontent.com/Skuzzle-UK/Waves/main/screenshot2.png",
    ],
  },
  {
    name: "MandoCode",
    description:
      "A CLI coding agent powered by Ollama + Semantic Kernel. Run locally or in the cloud. Refactors code, proposes diffs, and updates your project safely — no API keys required.",
    github: "DevMando/MandoCode",
    imageUrls: [
      "https://raw.githubusercontent.com/DevMando/MandoCode/main/docs/images/hero-demo.gif",
      "https://raw.githubusercontent.com/DevMando/MandoCode/main/docs/images/diff-approval.png",
      "https://raw.githubusercontent.com/DevMando/MandoCode/main/docs/images/task-planner.png",
      "https://raw.githubusercontent.com/DevMando/MandoCode/main/docs/images/music-player.png",
    ],
  },
  {
    name: "azure-servicebus-console",
    description:
      "Terminal UI for Azure Service Bus — browse namespaces, queues, topics and subscriptions, inspect and requeue dead-lettered messages, with keyboard and mouse navigation.",
    github: "zidad/azure-servicebus-console",
    imageUrls: [
      "https://raw.githubusercontent.com/zidad/azure-servicebus-console/main/docs/screenshots/queues.png",
      "https://raw.githubusercontent.com/zidad/azure-servicebus-console/main/docs/screenshots/message-detail.png",
      "https://raw.githubusercontent.com/zidad/azure-servicebus-console/main/docs/screenshots/all-subscriptions.png",
      "https://raw.githubusercontent.com/zidad/azure-servicebus-console/main/docs/screenshots/connection.png",
    ],
  },
]
