export interface ShowcaseProject {
  name: string
  description: string
  github?: string
  website?: string
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
    description: "A CLI coding agent powered by Ollama + Semantic Kernel. Run locally or in the cloud. Refactors code, proposes diffs, and updates your project safely — no API keys required.",
    github: "DevMando/MandoCode",
    imageUrls: [
      "https://raw.githubusercontent.com/DevMando/MandoCode/main/docs/images/hero-demo.gif",
      "https://raw.githubusercontent.com/DevMando/MandoCode/main/docs/images/diff-approval.png",
      "https://raw.githubusercontent.com/DevMando/MandoCode/main/docs/images/task-planner.png",
      "https://raw.githubusercontent.com/DevMando/MandoCode/main/docs/images/music-player.png",
    ]
  }
]
