import { Terminal } from "lucide-react"
import { useState, useEffect } from "react"

export default function ConsoleTitle() {
  const fullText = "RazorConsole"
  const [text, setText] = useState("")
  const [isDeleting, setIsDeleting] = useState(false)
  const [isPaused, setIsPaused] = useState(false)

  useEffect(() => {
    const typingSpeed = 120
    const deletingSpeed = 70
    const pauseDuration = 1500
    const initialPause = 1000

    let timer: number

    const currentLength = text.length

    if (currentLength === 0 && !isDeleting) {
      // Initial pause before typing
      setIsPaused(true)
      timer = window.setTimeout(() => {
        setIsPaused(false)
        setText(fullText[0])
      }, initialPause)
    } else if (!isDeleting && currentLength === fullText.length) {
      // Pause before deleting
      setIsPaused(true)
      timer = window.setTimeout(() => {
        setIsPaused(false)
        setIsDeleting(true)
      }, pauseDuration)
    } else if (isDeleting && currentLength === 0) {
      // Pause before typing
      setIsPaused(true)
      timer = window.setTimeout(() => {
        setIsPaused(false)
        setIsDeleting(false)
      }, pauseDuration)
    } else if (!isDeleting) {
      // Continue typing
      timer = window.setTimeout(() => {
        setText(fullText.slice(0, currentLength + 1))
      }, typingSpeed)
    } else {
      // Continue deleting
      timer = window.setTimeout(() => {
        setText(fullText.slice(0, currentLength - 1))
      }, deletingSpeed)
    }
    return () => clearTimeout(timer)
  }, [text, isDeleting])
  const progress = text.length / fullText.length

  const getIconColor = () => {
    const isDark = document.documentElement.classList.contains("dark")

    if (isDark) {
      const r = Math.round(96 + (192 - 96) * progress)
      const g = Math.round(165 + (132 - 165) * progress)
      const b = Math.round(250 + (252 - 250) * progress)
      return `rgb(${r}, ${g}, ${b})`
    } else {
      const r = Math.round(37 + (124 - 37) * progress)
      const g = Math.round(99 + (58 - 99) * progress)
      const b = Math.round(235 + (237 - 235) * progress)
      return `rgb(${r}, ${g}, ${b})`
    }
  }
  return (
    <h1 className="mb-4 flex items-center justify-center gap-3 text-5xl font-bold">
      <div className="relative inline-flex items-center">
        <div className="absolute inset-0 rounded-lg bg-gradient-to-r from-blue-500/20 to-violet-500/20 blur-xl dark:from-blue-600/40 dark:to-violet-600/40" />

        <Terminal
          className="relative z-10 h-12 w-12 transition-all duration-300"
          strokeWidth={2.5}
          style={{ color: getIconColor() }}
        />

        <span className="relative z-10 ml-3 inline-flex h-12 items-center">
          <span className="bg-gradient-to-r from-blue-600 to-violet-600 bg-clip-text text-transparent dark:from-blue-400 dark:to-violet-400">
            {text}
          </span>

          {/* Cursor */}
          <span
            className={`ml-0.5 inline-block h-10 w-0.5 bg-blue-600 dark:bg-blue-400 ${isPaused ? "animate-pulse" : "opacity-100"}`}
          />
        </span>
      </div>
    </h1>
  )
}
