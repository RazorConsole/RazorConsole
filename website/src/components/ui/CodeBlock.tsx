import { type BundledLanguage, createHighlighter, type Highlighter } from "shiki"
import { CopyButton } from "@/components/ui/CopyButton"
import { useEffect, useState } from "react"

let globalHighlighter: Highlighter | null = null

// For ssr, inits the shiki highlighter on the server 
export async function initHighlighter() {
  if (!globalHighlighter) {
    globalHighlighter = await createHighlighter({
      themes: ["github-dark", "github-light"],
      langs: ["csharp", "razor", "bash", "xml", "shell", "typescript", "javascript", "json"]
    })
  }
}

function generateHtml(code: string, language: BundledLanguage) {
  return globalHighlighter?.codeToHtml(code.trim(), {
    lang: language,
    themes: {
      light: "github-light",
      dark: "github-dark",
    },
    defaultColor: false,
  }).replace(/background-color:[^;"]+;?/g, "").replace(/background:[^;"]+;?/g, "") ?? "";
}

interface CodeBlockProps {
  code: string
  language?: BundledLanguage
  showCopy?: boolean
  className?: string
}

function CodeBlock({ code, language = "csharp", showCopy = false, className = "" }: CodeBlockProps) {
  let staticHtml = ""
  if (globalHighlighter) {
    staticHtml = generateHtml(code.trim(), language)  
  }

  const [html, setHtml] = useState(staticHtml);

  useEffect(() => {
    if (!globalHighlighter) {
      initHighlighter().then(() => {
        const newHtml = generateHtml(code.trim(), language);
        setHtml(newHtml);
      })
    }
  }, [code, language])

  return (
    <div
      className={`group relative my-6 overflow-hidden overflow-x-auto rounded-xl border border-slate-200 bg-slate-100 p-4 text-sm dark:border-slate-700 dark:bg-slate-900 ${className}`}
    >
      {showCopy && (
        <div className="absolute top-3 right-3 z-10 opacity-0 transition-opacity group-hover:opacity-100">
          <CopyButton content={code} />
        </div>
      )}

      <div
        className="text-sm leading-relaxed"
        dangerouslySetInnerHTML={{ __html: html || `<pre><code>${code}</code></pre>` }}
      />
    </div>
  )
}

export default CodeBlock