import { Terminal } from "lucide-react"
import { useState, useEffect, useRef } from "react"

interface Props {
  text: string
}

const LoadingOverlay: React.FC<Props> = ({ text }) => {
  const [dots, setDots] = useState("")
  const increasingRef = useRef(true)

  useEffect(() => {
    const interval = setInterval(() => {
      setDots((currentDots) => {
        if (increasingRef.current) {
          if (currentDots.length < 3) {
            return currentDots + "."
          } else {
            increasingRef.current = false
            return ".."
          }
        } else {
          if (currentDots.length > 1) {
            return currentDots.slice(0, -1)
          } else if (currentDots.length == 1) {
            return ""
          } else {
            increasingRef.current = true
            return ".."
          }
        }
      })
    }, 350)

    return () => clearInterval(interval)
  }, [])

  return (
    <div className="fixed inset-0 z-50 flex flex-col items-center justify-center bg-slate-50 select-none dark:bg-slate-950">
      <div className="pointer-events-none absolute flex items-center justify-center">
        <div className="h-40 w-40 rounded-full bg-gradient-to-r from-blue-500/20 to-violet-500/20 blur-3xl dark:from-blue-600/40 dark:to-violet-600/40" />
      </div>

      <div className="relative z-10 flex flex-col items-center">
        <div className="flex items-end justify-center pb-4">
          <Terminal className="mr-2 h-20 w-20 text-blue-600 dark:text-blue-400" strokeWidth={3} />

          <span className="mb-[2px] w-[3ch] bg-gradient-to-r from-blue-600 to-violet-600 bg-clip-text text-left font-mono text-6xl leading-none font-bold text-transparent dark:from-blue-400 dark:to-violet-400">
            {dots}
          </span>
        </div>

        <p className="animate-pulse bg-gradient-to-r from-blue-600 to-violet-600 bg-clip-text text-lg font-medium text-transparent dark:from-blue-400 dark:to-violet-400">
          {text}
        </p>
      </div>
    </div>
  )
}

export default LoadingOverlay
