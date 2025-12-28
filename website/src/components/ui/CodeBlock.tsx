import { useEffect, useState } from "react"
import { codeToHtml, type BundledLanguage } from "shiki"
import { useTheme } from "@/hooks/useTheme"
import { CopyButton } from "@/components/ui/CopyButton"

interface CodeBlockProps {
  code: string
  language?: BundledLanguage
  showCopy?: boolean
  className?: string
}

function CodeBlock({ code, language = "csharp", showCopy = true, className = "" }: CodeBlockProps) {
  const [html, setHtml] = useState("")
  const theme = useTheme((s) => s.theme)
  const resolvedTheme =
    theme === "system"
      ? window.matchMedia("(prefers-color-scheme: dark)").matches
        ? "dark"
        : "light"
      : theme

  useEffect(() => {
    codeToHtml(code, {
      lang: language,
      theme: resolvedTheme === "dark" ? "github-dark" : "github-light",
    }).then((generatedHtml) => {
      // remove inline background styles
      const cleanHtml = generatedHtml
        .replace(/background-color:[^;"]+;?/g, "")
        .replace(/background:[^;"]+;?/g, "")
      setHtml(cleanHtml)
    })
  }, [code, language, resolvedTheme])

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
        className="[&_code]:text-sm [&_code]:leading-relaxed"
        dangerouslySetInnerHTML={{ __html: html }}
      />
    </div>
  )
}

export default CodeBlock
