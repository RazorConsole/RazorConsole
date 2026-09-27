export interface ShowcaseProject {
  name: string
  description: string
  github?: string
  website?: string
  downloadUrl?: string
  videoUrl?: string
  installCommands?: Array<{
    label: string
    command: string
  }>
  imageUrls?: string[]
}

export const showcaseProjects: ShowcaseProject[] = [
  // Add your project here! Submit a PR to be featured.
  {
    name: "RazorConsole Snake",
    description:
      "A responsive Snake game and Native AOT showcase for RazorConsole, with keyboard and mouse controls, a draggable speed slider, and standalone builds for Windows, macOS, and Linux.",
    github: "RazorConsole/RazorConsole/tree/main/examples/SnakeGame",
    downloadUrl: "https://github.com/RazorConsole/RazorConsole/releases/latest",
    videoUrl: "showcase/snake-demo.mp4",
    installCommands: [
      {
        label: "macOS / Linux",
        command:
          "curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-snake.sh | sh",
      },
      {
        label: "Windows PowerShell",
        command:
          "irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-snake.ps1 | iex",
      },
    ],
  },
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
]
