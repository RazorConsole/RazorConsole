import { useState } from "react"
import { Check, Clipboard, Package, ShieldCheck } from "lucide-react"
import { Link } from "react-router-dom"
import { buttonVariants } from "@/components/ui/Button"
import { Card, CardContent } from "@/components/ui/Card"

const commands = {
  unix:
    "curl -fsSL https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-gallery.sh | sh",
  windows:
    "& ([scriptblock]::Create((irm https://raw.githubusercontent.com/RazorConsole/RazorConsole/main/scripts/install-razor-console-app.ps1))) -App Gallery",
} as const

type Platform = keyof typeof commands

export default function GalleryInstallSection() {
  const [platform, setPlatform] = useState<Platform>("unix")
  const [copied, setCopied] = useState(false)

  const copyCommand = async () => {
    await navigator.clipboard.writeText(commands[platform])
    setCopied(true)
    window.setTimeout(() => setCopied(false), 2000)
  }

  return (
    <Card className="mb-16 overflow-hidden border-violet-200 dark:border-violet-900/70">
      <CardContent className="grid gap-8 bg-gradient-to-br from-violet-50 via-white to-blue-50 p-6 md:grid-cols-[minmax(0,0.8fr)_minmax(0,1.2fr)] md:items-center md:p-8 dark:from-violet-950/40 dark:via-slate-950 dark:to-blue-950/30">
        <div>
          <div className="mb-3 flex items-center gap-2 text-sm font-semibold text-violet-700 dark:text-violet-300">
            <Package className="h-4 w-4" aria-hidden="true" />
            Component Gallery
          </div>
          <h2 className="mb-3 text-2xl font-bold tracking-tight text-slate-950 dark:text-white">
            Explore every component in your terminal
          </h2>
          <p className="mb-5 text-slate-600 dark:text-slate-300">
            Install the native Gallery for Windows, macOS, or Linux.
          </p>
          <div className="mb-6 flex flex-wrap gap-2 text-xs font-medium text-slate-600 dark:text-slate-300">
            <span className="rounded-full border border-slate-200 bg-white/80 px-3 py-1 dark:border-slate-700 dark:bg-slate-900/70">
              x64 + Arm64
            </span>
            <span className="flex items-center gap-1.5 rounded-full border border-slate-200 bg-white/80 px-3 py-1 dark:border-slate-700 dark:bg-slate-900/70">
              <ShieldCheck className="h-3.5 w-3.5 text-emerald-600 dark:text-emerald-400" aria-hidden="true" />
              SHA-256 verified
            </span>
          </div>
          <Link to="/docs/component-gallery" className={buttonVariants({ variant: "outline" })}>
            Installation details
          </Link>
        </div>

        <div className="min-w-0 overflow-hidden rounded-xl border border-slate-800 bg-slate-950 shadow-lg shadow-slate-950/10">
          <div className="flex items-center justify-between border-b border-slate-800 px-3 pt-2">
            <div className="flex" role="tablist" aria-label="Installation platform">
              <button
                type="button"
                role="tab"
                aria-selected={platform === "unix"}
                onClick={() => {
                  setPlatform("unix")
                  setCopied(false)
                }}
                className={`border-b-2 px-3 py-2 text-sm font-medium transition-colors ${
                  platform === "unix"
                    ? "border-violet-400 text-white"
                    : "border-transparent text-slate-400 hover:text-slate-200"
                }`}
              >
                macOS / Linux
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={platform === "windows"}
                onClick={() => {
                  setPlatform("windows")
                  setCopied(false)
                }}
                className={`border-b-2 px-3 py-2 text-sm font-medium transition-colors ${
                  platform === "windows"
                    ? "border-violet-400 text-white"
                    : "border-transparent text-slate-400 hover:text-slate-200"
                }`}
              >
                Windows
              </button>
            </div>
            <button
              type="button"
              onClick={copyCommand}
              className="mb-2 flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium text-slate-300 transition-colors hover:bg-slate-800 hover:text-white"
              aria-label="Copy installation command"
            >
              {copied ? (
                <Check className="h-3.5 w-3.5 text-emerald-400" aria-hidden="true" />
              ) : (
                <Clipboard className="h-3.5 w-3.5" aria-hidden="true" />
              )}
              {copied ? "Copied" : "Copy"}
            </button>
          </div>
          <div role="tabpanel" className="p-5">
            <code className="block break-all whitespace-pre-wrap font-mono text-sm leading-6 text-slate-200">
              <span className="select-none text-violet-400">$ </span>
              {commands[platform]}
            </code>
          </div>
        </div>
      </CardContent>
    </Card>
  )
}
