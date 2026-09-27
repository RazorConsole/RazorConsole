import { Button } from "@/components/ui/Button"
import type { ShowcaseProject } from "@/data/showcase"
import { Check, Clipboard, Download, ExternalLink, Terminal, X } from "lucide-react"
import { useEffect, useRef, useState } from "react"

type InstallCommand = NonNullable<ShowcaseProject["installCommands"]>[number]

export default function InstallDialog({
  project,
  onClose,
}: {
  project: ShowcaseProject
  onClose: () => void
}) {
  const commands = project.installCommands ?? []
  const [selectedCommand, setSelectedCommand] = useState<InstallCommand | undefined>(commands[0])
  const [copied, setCopied] = useState(false)
  const closeButtonRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose()
    }

    document.addEventListener("keydown", handleKeyDown)
    document.body.style.overflow = "hidden"
    closeButtonRef.current?.focus()

    return () => {
      document.removeEventListener("keydown", handleKeyDown)
      document.body.style.overflow = ""
    }
  }, [onClose])

  const copyCommand = async () => {
    if (!selectedCommand) return
    await navigator.clipboard.writeText(selectedCommand.command)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 2000)
  }

  return (
    <div
      className="fixed inset-0 z-50 flex items-end justify-center bg-slate-950/65 p-0 backdrop-blur-sm sm:items-center sm:p-6"
      role="presentation"
      onMouseDown={(event) => {
        if (event.currentTarget === event.target) onClose()
      }}
    >
      <section
        role="dialog"
        aria-modal="true"
        aria-labelledby="install-dialog-title"
        className="w-full max-w-2xl overflow-hidden rounded-t-2xl border border-slate-200 bg-white shadow-2xl sm:rounded-2xl dark:border-slate-700 dark:bg-slate-900"
      >
        <div className="flex items-start justify-between border-b border-slate-200 px-5 py-5 sm:px-6 dark:border-slate-800">
          <div className="flex min-w-0 gap-3.5">
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-violet-100 text-violet-700 dark:bg-violet-500/15 dark:text-violet-300">
              <Terminal className="h-5 w-5" aria-hidden="true" />
            </div>
            <div>
              <h2
                id="install-dialog-title"
                className="text-xl font-semibold tracking-tight text-slate-950 dark:text-white"
              >
                Install {project.name}
              </h2>
              <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
                Choose your platform, then run the command in your terminal.
              </p>
            </div>
          </div>
          <button
            ref={closeButtonRef}
            type="button"
            onClick={onClose}
            className="ml-3 rounded-lg p-2 text-slate-500 transition-colors hover:bg-slate-100 hover:text-slate-950 focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none dark:hover:bg-slate-800 dark:hover:text-white"
            aria-label="Close installation instructions"
          >
            <X className="h-5 w-5" aria-hidden="true" />
          </button>
        </div>

        <div className="p-5 sm:p-6">
          <div
            className="mb-4 inline-flex rounded-lg bg-slate-100 p-1 dark:bg-slate-800"
            role="tablist"
            aria-label="Installation platform"
          >
            {commands.map((install) => (
              <button
                key={install.label}
                type="button"
                role="tab"
                aria-selected={selectedCommand?.label === install.label}
                onClick={() => {
                  setSelectedCommand(install)
                  setCopied(false)
                }}
                className={`rounded-md px-3.5 py-2 text-sm font-medium transition-all focus-visible:ring-2 focus-visible:ring-violet-500 focus-visible:outline-none ${
                  selectedCommand?.label === install.label
                    ? "bg-white text-slate-950 shadow-sm dark:bg-slate-700 dark:text-white"
                    : "text-slate-500 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-100"
                }`}
              >
                {install.label}
              </button>
            ))}
          </div>

          {selectedCommand && (
            <div className="overflow-hidden rounded-xl border border-slate-800 bg-slate-950 shadow-lg shadow-slate-950/10">
              <div className="flex items-center justify-between border-b border-slate-800 px-4 py-2.5">
                <div className="flex gap-1.5" aria-hidden="true">
                  <span className="h-2.5 w-2.5 rounded-full bg-rose-400" />
                  <span className="h-2.5 w-2.5 rounded-full bg-amber-400" />
                  <span className="h-2.5 w-2.5 rounded-full bg-emerald-400" />
                </div>
                <button
                  type="button"
                  onClick={copyCommand}
                  className="inline-flex items-center gap-1.5 rounded-md px-2.5 py-1.5 text-xs font-medium text-slate-300 transition-colors hover:bg-slate-800 hover:text-white focus-visible:ring-2 focus-visible:ring-violet-400 focus-visible:outline-none"
                >
                  {copied ? (
                    <Check className="h-3.5 w-3.5 text-emerald-400" aria-hidden="true" />
                  ) : (
                    <Clipboard className="h-3.5 w-3.5" aria-hidden="true" />
                  )}
                  {copied ? "Copied" : "Copy"}
                </button>
              </div>
              <div className="max-h-48 overflow-auto p-4 sm:p-5">
                <code className="block font-mono text-sm leading-6 break-all whitespace-pre-wrap text-slate-200">
                  <span className="text-violet-400 select-none">$ </span>
                  {selectedCommand.command}
                </code>
              </div>
            </div>
          )}

          <div className="mt-5 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <p className="text-xs leading-5 text-slate-500 dark:text-slate-400">
              The installer selects the correct binary for your operating system and architecture.
            </p>
            {project.downloadUrl && (
              <a
                href={project.downloadUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex shrink-0 items-center gap-1.5 text-sm font-medium text-violet-700 hover:underline dark:text-violet-300"
              >
                <Download className="h-4 w-4" aria-hidden="true" />
                Manual download
                <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
              </a>
            )}
          </div>
        </div>

        <div className="flex justify-end border-t border-slate-200 bg-slate-50 px-5 py-4 sm:px-6 dark:border-slate-800 dark:bg-slate-950/50">
          <Button type="button" variant="outline" onClick={onClose}>
            Done
          </Button>
        </div>
      </section>
    </div>
  )
}
