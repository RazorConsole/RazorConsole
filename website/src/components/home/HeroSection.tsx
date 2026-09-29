import { useId } from "react"
import XTermPreview from "@/components/components/XTermPreview"
import ConsoleTitle from "@/components/home/ConsoleTitle"
import CodeBlock from "@/components/ui/CodeBlock"
import { CopyButton } from "@/components/ui/CopyButton"
import { Link } from "@/components/ui/SiteLink"

const demoSnippet = `<div data-focusable="true"
     @onkeydown="OnKey"
     @onmouseenter="() => _hovered = true"
     @onclick="() => _clicks++">
    <Panel BorderColor="@(_hovered ? Color.Yellow : Color.Blue)">
        <Markup Content="@($"Key: {_key} · Clicks: {_clicks}")" />
    </Panel>
</div>

@code {
    bool _hovered;
    int _clicks;
    string _key = "none";
    void OnKey(KeyboardEventArgs e) => _key = e.Key;
}`

export default function HeroSection() {
  const instanceId = `home-demo-${useId().replace(/[^a-zA-Z0-9_-]/g, "")}`

  return (
    <section className="mb-16" aria-labelledby="home-hero-title">
      <div className="text-center">
        <ConsoleTitle />
        <h1 id="home-hero-title" className="mx-auto mb-6 max-w-3xl text-3xl font-bold sm:text-4xl">
          Build C# terminal UIs with Razor components
        </h1>
        <p className="mx-auto mb-10 max-w-2xl text-xl text-slate-600 dark:text-slate-300">
          Bring a familiar web-development experience to .NET terminal apps.
          Compose reusable Razor components, manage state in C#, and handle input with built-in events.
        </p>
      </div>

      <section className="mx-auto mb-10 max-w-5xl" aria-labelledby="why-razorconsole">
        <h2 id="why-razorconsole" className="mb-4 text-center text-2xl font-semibold">
          Why choose RazorConsole?
        </h2>
        <div className="grid gap-6 text-slate-600 md:grid-cols-3 dark:text-slate-300">
          <div>
            <h3 className="mb-2 font-semibold text-slate-900 dark:text-white">Razor components for the terminal</h3>
            <p>Use the component model you know from web development for interactive tools,
              dashboards, and forms. Think "Ink for .NET" as an analogy for component-based TUIs,
              not an official port, affiliation, or API compatibility claim.</p>
            <Link className="text-blue-600 underline dark:text-blue-400" to="/docs/tutorial/hello-world">
              Build your first TUI
            </Link>
          </div>
          <div>
            <h3 className="mb-2 font-semibold text-slate-900 dark:text-white">Built-in mouse and keyboard events</h3>
            <p>Wire up clicks, hover, and key handlers directly in Razor.
              Follow the setup and terminal-support guidance for interactive input.</p>
            <Link className="text-blue-600 underline dark:text-blue-400" to="/docs/tutorial/mouse-events">
              Add mouse interaction
            </Link>{" and "}
            <Link className="text-blue-600 underline dark:text-blue-400" to="/blog/keyboard-events">
              keyboard events
            </Link>
          </div>
          <div>
            <h3 className="mb-2 font-semibold text-slate-900 dark:text-white">NativeAOT-compatible distribution</h3>
            <p>Publish native apps that need no installed .NET runtime and avoid JIT warm-up at startup.
              Support is experimental: check platform build tools, trimming, and third-party dependencies.</p>
            <Link className="text-blue-600 underline dark:text-blue-400" to="/blog/native-aot">
              Read Native AOT requirements
            </Link>
          </div>
        </div>
        <p className="mt-6 text-center text-sm text-slate-600 dark:text-slate-300">
          Choose RazorConsole when you want Razor composition and event-driven interaction in a C# terminal UI.
          Spectre.Console is part of its rendering foundation, not a mutually exclusive alternative.
          {" "}<Link className="text-blue-600 underline dark:text-blue-400" to="/guides/choosing-dotnet-tui">
            Compare .NET terminal UI approaches
          </Link>.
        </p>
      </section>

      <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-lg shadow-slate-950/5 dark:border-slate-800 dark:bg-slate-950">
        <div className="grid min-w-0 lg:grid-cols-2">
          <div className="min-w-0 border-b border-slate-200 lg:border-r lg:border-b-0 dark:border-slate-800">
            <div className="flex h-11 items-center justify-between border-b border-slate-200 px-4 dark:border-slate-800">
              <span className="text-xs font-semibold tracking-wide text-slate-600 uppercase dark:text-slate-300">
                Code
              </span>
              <CopyButton content={demoSnippet} />
            </div>
            <CodeBlock code={demoSnippet} language="razor" embedded className="h-[300px]" />
          </div>

          <div className="min-w-0">
            <div className="flex h-11 items-center justify-between border-b border-slate-200 px-4 dark:border-slate-800">
              <span className="text-xs font-semibold tracking-wide text-slate-600 uppercase dark:text-slate-300">
                Preview
              </span>
              <span className="text-xs text-slate-500 dark:text-slate-400">Keyboard + mouse</span>
            </div>
            <XTermPreview
              elementId={instanceId}
              componentId="HomeDemo"
              embedded
              className="h-[300px]"
            />
          </div>
        </div>
      </div>
    </section>
  )
}
