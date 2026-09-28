import { useId } from "react"
import XTermPreview from "@/components/components/XTermPreview"
import ConsoleTitle from "@/components/home/ConsoleTitle"
import CodeBlock from "@/components/ui/CodeBlock"

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
        <div id="home-hero-title">
          <ConsoleTitle />
        </div>
        <p className="mx-auto mb-10 max-w-2xl text-xl text-slate-600 dark:text-slate-300">
          Build rich, interactive console applications using familiar Razor syntax and the power of
          Spectre.Console
        </p>
      </div>

      <div className="grid min-w-0 gap-6 lg:grid-cols-2 lg:items-stretch">
        <div className="min-w-0">
          <div className="mb-3 text-sm font-semibold text-slate-700 dark:text-slate-300">
            Interactive Razor in 15 lines
          </div>
          <CodeBlock
            code={demoSnippet}
            language="razor"
            showCopy
            className="my-0 h-[320px] overflow-auto"
          />
        </div>

        <div className="min-w-0">
          <div className="mb-3 text-sm font-semibold text-slate-700 dark:text-slate-300">
            Live preview · use the keyboard or mouse
          </div>
          <XTermPreview elementId={instanceId} componentId="HomeDemo" className="h-[320px]" />
        </div>
      </div>
    </section>
  )
}
