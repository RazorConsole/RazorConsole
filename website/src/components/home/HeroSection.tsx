import { Package, Github, Terminal } from "lucide-react"
import { Link } from "react-router-dom"
import { Button } from "@/components/ui/Button"
import ConsoleTitle from "@/components/home/ConsoleTitle"

export default function HeroSection() {
  return (
    <div className="mb-16 text-center">
      <ConsoleTitle />
      <p className="mx-auto mb-8 max-w-2xl text-xl text-slate-600 dark:text-slate-300">
        Build rich, interactive console applications using familiar Razor syntax and the power of
        Spectre.Console
      </p>
      <div className="flex flex-wrap justify-center gap-4">
        <Link to="/docs#quick-start">
          <Button size="lg" className="gap-2">
            <Terminal className="h-4 w-4" />
            Quick Start
          </Button>
        </Link>
        <Link to="/components">
          <Button size="lg" variant="outline" className="gap-2">
            <Package className="h-4 w-4" />
            Browse Components
          </Button>
        </Link>
        <a
          href="https://github.com/RazorConsole/RazorConsole"
          target="_blank"
          rel="noopener noreferrer"
        >
          <Button size="lg" variant="secondary" className="gap-2">
            <Github className="h-4 w-4" />
            GitHub
          </Button>
        </a>
      </div>
    </div>
  )
}
