import { Card, CardHeader, CardTitle, CardDescription, CardContent } from "@/components/ui/Card"

export default function AdvancedTopicsSection() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Examples</CardTitle>
        <CardDescription>Real-world applications</CardDescription>
      </CardHeader>
      <CardContent className="space-y-2">
        <a
          href="https://github.com/RazorConsole/RazorConsole/tree/main/examples/LLMAgentTUI"
          target="_blank"
          rel="noopener noreferrer"
          className="block text-slate-700 transition-colors hover:text-blue-600 dark:text-slate-300 dark:hover:text-blue-400"
        >
          • LLM Agent TUI - Claude Code-inspired chat interface
        </a>
        <a
          href="https://github.com/RazorConsole/RazorConsole/tree/main/src/RazorConsole.Gallery"
          target="_blank"
          rel="noopener noreferrer"
          className="block text-slate-700 transition-colors hover:text-blue-600 dark:text-slate-300 dark:hover:text-blue-400"
        >
          • Interactive Component Gallery
        </a>
      </CardContent>
    </Card>
  )
}
