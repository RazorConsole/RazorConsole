import { ArrowRight } from "lucide-react"
import { Link } from "@/components/ui/SiteLink"
import { Button } from "@/components/ui/Button"
import { Card, CardContent } from "@/components/ui/Card"
import CodeBlock from "@/components/ui/CodeBlock"
import { CopyButton } from "@/components/ui/CopyButton"

const installSnippet = `claude plugin marketplace add RazorConsole/RazorConsole
claude plugin install razorconsole@razorconsole`

const linkClass = "rounded-sm text-blue-600 underline underline-offset-4 hover:text-blue-700 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-blue-600 dark:text-blue-400 dark:hover:text-blue-300 dark:focus-visible:outline-blue-400"

export default function AgentPluginSection() {
  return (
    <section className="mb-16" aria-labelledby="home-agent-title">
      <Card className="border-violet-200 dark:border-violet-900/70">
        <CardContent className="grid gap-6 p-6 sm:p-8 lg:grid-cols-2 lg:items-center">
          <div>
            <h2 id="home-agent-title" className="mb-2 text-2xl font-bold tracking-tight text-slate-950 dark:text-white">
              Build with an AI agent
            </h2>
            <p className="mb-4 max-w-xl text-slate-600 dark:text-slate-300">
              The RazorConsole plugin gives a coding agent the documentation and a way to run your app.
              It reads the docs before it writes a component, then starts the real app in a terminal,
              reads the screen and the component tree, clicks, and asserts the result.
              It installs in Claude Code, GitHub Copilot, OpenAI Codex, Google Antigravity and VS Code.
            </p>
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
              <Link to="/blog/develop-with-an-agent">
                <Button className="w-full gap-2 sm:w-auto">
                  Follow the agent tutorial
                  <ArrowRight className="h-4 w-4" aria-hidden="true" />
                </Button>
              </Link>
              <a
                className={linkClass}
                href="https://github.com/RazorConsole/RazorConsole/tree/main/plugins/razorconsole"
              >
                Plugin on GitHub
              </a>
            </div>
            <p className="mt-4 text-sm text-slate-600 dark:text-slate-300">
              The plugin runs <code>razorconsole mcp</code>, so it needs the{" "}
              <Link className={linkClass} to="/blog/cli">command-line tool</Link> installed first.
            </p>
          </div>
          <div className="min-w-0 overflow-hidden rounded-xl border border-slate-200 dark:border-slate-800">
            <div className="flex h-11 items-center justify-between border-b border-slate-200 px-4 dark:border-slate-800">
              <span className="text-xs font-semibold tracking-wide text-slate-600 uppercase dark:text-slate-300">
                Claude Code
              </span>
              <CopyButton content={installSnippet} />
            </div>
            <CodeBlock code={installSnippet} language="bash" embedded className="h-[96px]" />
          </div>
        </CardContent>
      </Card>
    </section>
  )
}
