import { useLoaderData, type LoaderFunctionArgs, type MetaFunction } from "react-router"
import { Link } from "@/components/ui/SiteLink"
import { MarkdownRenderer } from "@/components/ui/Markdown"
import { guides } from "@/data/guides"
import { getPageUrl } from "@/lib/utils"

const modules = import.meta.glob("/src/guides/*.md", { query: "?raw", import: "default" })

export async function loader({ params }: LoaderFunctionArgs) {
  if (!params.slug) return { guide: undefined, content: "" }
  const guide = guides.find((entry) => entry.slug === params.slug)
  if (!guide) throw new Response("Not Found", { status: 404 })
  const load = modules[`/src/guides/${guide.slug}.md`]
  if (!load) throw new Error(`Missing guide content: ${guide.slug}`)
  return { guide, content: await load() as string }
}

export const meta: MetaFunction<typeof loader> = ({ data, location }) => {
  const title = `${data?.guide?.title ?? "C# and .NET terminal UI guides"} | RazorConsole`
  const description = data?.guide?.description ?? "Learn to build C# terminal UIs, choose a .NET TUI library, handle input, and distribute Razor-based console applications."
  return [
    { title },
    { name: "description", content: description },
    { property: "og:title", content: title },
    { property: "og:description", content: description },
    { property: "og:url", content: getPageUrl(location.pathname) },
  ]
}

export default function Guides() {
  const { guide, content } = useLoaderData<typeof loader>()
  return (
    <main className="mx-auto max-w-5xl px-6 py-12">
      {guide ? (
        <>
          <Link to="/guides" className="mb-6 inline-block text-blue-600 underline">All terminal UI guides</Link>
          <article className="prose prose-slate max-w-none dark:prose-invert">
            <MarkdownRenderer content={content} />
          </article>
        </>
      ) : (
        <>
          <h1 className="mb-5 text-4xl font-bold">C# and .NET terminal UI guides</h1>
          <p className="mb-8 text-lg">Start with the programming model, then choose the input and deployment
            approach that fits your application. These guides connect practical decisions to working examples.</p>
          <div className="grid gap-6 md:grid-cols-2">
            {guides.map((entry) => (
              <section key={entry.slug} className="rounded-xl border border-slate-300 p-6 dark:border-slate-700">
                <h2 className="mb-3 text-xl font-semibold">
                  <Link to={`/guides/${entry.slug}`} className="text-blue-600 underline dark:text-blue-400">{entry.title}</Link>
                </h2>
                <p>{entry.description}</p>
              </section>
            ))}
          </div>
          <h2 className="mt-10 mb-4 text-2xl font-semibold">Go from reading to a running app</h2>
          <ul className="list-inside list-disc space-y-3">
            <li><Link className="text-blue-600 underline" to="/docs/tutorial/hello-world">Interactive C# terminal UI tutorial</Link></li>
            <li><Link className="text-blue-600 underline" to="/docs/tutorial/mouse-events">Mouse events, hover, wheel, and drag</Link></li>
            <li><Link className="text-blue-600 underline" to="/blog/keyboard-events">Keyboard event handling</Link></li>
            <li><Link className="text-blue-600 underline" to="/blog/native-aot">NativeAOT publishing and limitations</Link></li>
          </ul>
        </>
      )}
    </main>
  )
}
