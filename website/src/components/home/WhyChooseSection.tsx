import { Card, CardContent, CardFooter, CardHeader, CardTitle } from "@/components/ui/Card"
import { Link } from "@/components/ui/SiteLink"

const linkClass = "rounded-sm text-blue-600 underline underline-offset-4 hover:text-blue-700 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-blue-600 dark:text-blue-400 dark:hover:text-blue-300 dark:focus-visible:outline-blue-400"

export default function WhyChooseSection() {
  return (
    <section className="mt-12 w-full" aria-labelledby="why-razorconsole">
      <h2 id="why-razorconsole" className="mb-6 text-center text-2xl font-semibold tracking-tight sm:text-3xl">
        Why choose RazorConsole?
      </h2>
      <div className="grid grid-cols-1 gap-6 lg:grid-cols-3">
        <Card className="flex min-w-0 flex-col rounded-2xl">
          <CardHeader>
            <CardTitle className="text-xl leading-snug">Razor components for the terminal</CardTitle>
          </CardHeader>
          <CardContent className="flex-1 leading-relaxed text-slate-600 dark:text-slate-300">
            <p>Use the component model you know from web development for interactive tools,
              dashboards, and forms.</p>
          </CardContent>
          <CardFooter className="block text-sm font-medium">
            <Link className={linkClass} to="/docs/tutorial/hello-world">Build your first TUI</Link>
          </CardFooter>
        </Card>
        <Card className="flex min-w-0 flex-col rounded-2xl">
          <CardHeader>
            <CardTitle className="text-xl leading-snug">Built-in mouse and keyboard events</CardTitle>
          </CardHeader>
          <CardContent className="flex-1 leading-relaxed text-slate-600 dark:text-slate-300">
            <p>Wire up clicks, hover, and key handlers directly in Razor.
              Follow the setup and terminal-support guidance for interactive input.</p>
          </CardContent>
          <CardFooter className="block text-sm font-medium">
            <Link className={linkClass} to="/docs/tutorial/mouse-events">Add mouse interaction</Link>
            {" and "}
            <Link className={linkClass} to="/blog/keyboard-events">keyboard events</Link>
          </CardFooter>
        </Card>
        <Card className="flex min-w-0 flex-col rounded-2xl">
          <CardHeader>
            <CardTitle className="text-xl leading-snug">NativeAOT-compatible distribution</CardTitle>
          </CardHeader>
          <CardContent className="flex-1 leading-relaxed text-slate-600 dark:text-slate-300">
            <p>Publish native apps that need no installed .NET runtime and avoid JIT warm-up at startup.
              Support is experimental: check platform build tools, trimming, and third-party dependencies.</p>
          </CardContent>
          <CardFooter className="block text-sm font-medium">
            <Link className={linkClass} to="/blog/native-aot">Read Native AOT requirements</Link>
          </CardFooter>
        </Card>
      </div>
      <p className="mx-auto mt-6 max-w-3xl text-center text-sm leading-relaxed text-slate-600 dark:text-slate-300">
        Choose RazorConsole when you want Razor composition and event-driven interaction in a C# terminal UI.
        Spectre.Console is part of its rendering foundation, not a mutually exclusive alternative.
        {" "}<Link className={linkClass} to="/blog/choosing-dotnet-tui">
          Compare .NET terminal UI approaches
        </Link>.
      </p>
    </section>
  )
}
