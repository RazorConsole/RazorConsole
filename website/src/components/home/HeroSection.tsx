import { useId } from "react"
import XTermPreview from "@/components/components/XTermPreview"
import ConsoleTitle from "@/components/home/ConsoleTitle"
import CodeBlock from "@/components/ui/CodeBlock"
import { CopyButton } from "@/components/ui/CopyButton"
import WhyChooseSection from "@/components/home/WhyChooseSection"

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
          Build TUI with Razor Component
        </h1>
        <p className="mx-auto mb-10 max-w-2xl text-xl text-slate-600 dark:text-slate-300">
          Bring a familiar web-development experience to .NET terminal apps.
          Compose reusable Razor components, manage state in C#, and handle input with built-in events.
        </p>
      </div>

      <div id="home-demo" className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-lg shadow-slate-950/5 dark:border-slate-800 dark:bg-slate-950">
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
      <WhyChooseSection />
    </section>
  )
}
