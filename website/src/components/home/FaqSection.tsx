import { Card } from "@/components/ui/Card"
import { Link } from "@/components/ui/SiteLink"

const linkClass = "rounded-sm text-blue-600 underline underline-offset-4 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-blue-600 dark:text-blue-400 dark:focus-visible:outline-blue-400"
const summaryClass = "cursor-pointer rounded-lg px-6 py-5 text-lg font-semibold marker:text-blue-600 hover:bg-slate-50 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-blue-600 dark:marker:text-blue-400 dark:hover:bg-slate-900 dark:focus-visible:outline-blue-400"
const answerClass = "px-6 pb-6 leading-relaxed text-slate-600 dark:text-slate-300"

export default function FaqSection() {
  return (
    <section className="mt-16 w-full" aria-labelledby="home-faq-title">
      <h2 id="home-faq-title" className="mb-6 text-center text-2xl font-semibold tracking-tight sm:text-3xl">
        Frequently asked questions
      </h2>
      <div className="space-y-4">
        <Card>
          <details>
            <summary className={summaryClass}>What is RazorConsole?</summary>
            <p className={answerClass}>
              RazorConsole is a C#/.NET framework for terminal user interfaces built with Razor components.
              It renders to terminal cells, not browser HTML. See the{" "}
              <Link className={linkClass} to="/docs/tutorial/hello-world">interactive tutorial</Link>{" "}
              for an introduction.
            </p>
          </details>
        </Card>
        <Card>
          <details>
            <summary className={summaryClass}>Do I need a web server or Blazor hosting?</summary>
            <p className={answerClass}>
              No. A native RazorConsole application runs in the terminal without a web server.
              You can reuse your Razor component experience, but terminal layout and supported components
              differ from the browser: existing web pages cannot be assumed to work unchanged.
              The website's browser demos are a separate way to try the framework. Start with the{" "}
              <Link className={linkClass} to="/docs/tutorial/hello-world">Hello World tutorial</Link>.
            </p>
          </details>
        </Card>
        <Card>
          <details>
            <summary className={summaryClass}>How is it related to Spectre.Console?</summary>
            <p className={answerClass}>
              Spectre.Console is part of RazorConsole's rendering foundation. RazorConsole adds a Razor
              component authoring and interaction model; it is not a replacement for every use of
              Spectre.Console. The{" "}
              <Link className={linkClass} to="/blog/choosing-dotnet-tui">.NET TUI comparison</Link>{" "}
              explains when to evaluate each approach.
            </p>
          </details>
        </Card>
        <Card>
          <details>
            <summary className={summaryClass}>Does it support mouse and keyboard input?</summary>
            <p className={answerClass}>
              Yes. Built-in events let Razor components handle keys, clicks, and hover. Focus, terminal
              capabilities, and configuration still matter; native hosts must enable mouse reporting.
              Follow the{" "}
              <Link className={linkClass} to="/blog/keyboard-events">keyboard guide</Link>{" and "}
              <Link className={linkClass} to="/docs/tutorial/mouse-events">mouse tutorial</Link>,
              and keep a keyboard path for essential actions.
            </p>
          </details>
        </Card>
        <Card>
          <details>
            <summary className={summaryClass}>Can I publish with NativeAOT?</summary>
            <p className={answerClass}>
              Yes, with experimental support. Native publishing can remove the need for an installed .NET
              runtime and avoid JIT warm-up, but does not guarantee a measured startup time. Check
              platform build tools, trimming and reflection, third-party dependencies, and required
              assets. Build for each target platform and read the{" "}
              <Link className={linkClass} to="/blog/native-aot">Native AOT requirements</Link>.
            </p>
          </details>
        </Card>
        <Card>
          <details>
            <summary className={summaryClass}>How do I get started?</summary>
            <p className={answerClass}>
              Follow the{" "}
              <Link className={linkClass} to="/docs/tutorial/hello-world">interactive tutorial</Link>{" "}
              to build your first component, then explore state, input, and layout. Browse{" "}
              <Link className={linkClass} to="/components">component examples</Link>{" "}
              to see what you can compose.
            </p>
          </details>
        </Card>
      </div>
    </section>
  )
}
