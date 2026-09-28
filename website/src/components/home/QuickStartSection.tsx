import { ArrowRight } from "lucide-react"
import { Link } from "react-router-dom"
import { Button } from "@/components/ui/Button"
import { Card, CardContent } from "@/components/ui/Card"

export default function QuickStartSection() {
  return (
    <Card className="mb-16 border-violet-200 dark:border-violet-900/70">
      <CardContent className="flex flex-col gap-5 p-6 sm:flex-row sm:items-center sm:justify-between sm:p-8">
        <div>
          <h2 className="mb-2 text-2xl font-bold tracking-tight text-slate-950 dark:text-white">
            Quick Start
          </h2>
          <p className="max-w-2xl text-slate-600 dark:text-slate-300">
            Build and run your first RazorConsole application with the guided tutorial.
          </p>
        </div>
        <Link to="/docs/tutorial/hello-world">
          <Button className="w-full shrink-0 gap-2 sm:w-auto">
            Start the tutorial
            <ArrowRight className="h-4 w-4" aria-hidden="true" />
          </Button>
        </Link>
      </CardContent>
    </Card>
  )
}
