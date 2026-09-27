import { ArrowRight, Play, Terminal } from "lucide-react"
import { Link } from "react-router-dom"
import { Button } from "@/components/ui/Button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/Card"

export default function QuickStartSection() {
  return (
    <Card className="mb-16 overflow-hidden border-violet-200 dark:border-violet-900/70">
      <CardHeader className="bg-gradient-to-r from-violet-50 to-blue-50 dark:from-violet-950/40 dark:to-blue-950/30">
        <div className="mb-2 flex items-center gap-2 text-sm font-semibold text-violet-700 dark:text-violet-300">
          <Play className="h-4 w-4" aria-hidden="true" />
          Preview first
        </div>
        <CardTitle>Quick Start · Hello World</CardTitle>
        <CardDescription>
          Try a real RazorConsole component before building it locally.
        </CardDescription>
      </CardHeader>
      <CardContent className="grid gap-6 pt-6 md:grid-cols-[1fr_auto] md:items-center">
        <div className="space-y-3 text-sm leading-6 text-slate-600 dark:text-slate-300">
          <p className="flex items-start gap-2">
            <Terminal className="mt-1 h-4 w-4 shrink-0 text-violet-600" aria-hidden="true" />
            Interact with the same Razor component used by the native terminal runner.
          </p>
          <p>Then follow the complete project setup, source, exercise, and troubleshooting guide.</p>
        </div>
        <Link to="/docs/tutorial/hello-world">
          <Button className="w-full gap-2 md:w-auto">
            Open live tutorial
            <ArrowRight className="h-4 w-4" aria-hidden="true" />
          </Button>
        </Link>
      </CardContent>
    </Card>
  )
}
