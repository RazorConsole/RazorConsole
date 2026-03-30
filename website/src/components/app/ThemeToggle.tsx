import { Moon, Sun, Monitor, LoaderIcon } from "lucide-react"
import { Button } from "@/components/ui/Button"
import { useTheme } from "@/hooks/useTheme"
import { useEffect, useState } from "react"

export function ThemeToggle() {
  const theme = useTheme((state) => state.theme)
  const setTheme = useTheme((state) => state.setTheme)
  
  const [mounted, setMounted] = useState(false)

  useEffect(() => {
    setMounted(true)
  }, [])

  const cycleTheme = () => {
    if (theme === "light") {
      setTheme("dark")
    } else if (theme === "dark") {
      setTheme("system")
    } else {
      setTheme("light")
    }
  }

  const getIcon = () => {
    if (!mounted) return <LoaderIcon className="h-4 w-4" />
    switch (theme) {
      case "light": return <Sun className="h-4 w-4" />
      case "dark": return <Moon className="h-4 w-4" />
      case "system": return <Monitor className="h-4 w-4" />
    }
  }

  const getLabel = () => {
    if (!mounted) return "Loading..." 
    switch (theme) {
      case "light": return "Light"
      case "dark": return "Dark"
      case "system": return "System"
    }
  }

  return (
    <Button
      suppressHydrationWarning
      aria-label="Toggle light/dark theme"
      variant="ghost"
      size="sm"
      onClick={cycleTheme}
      title={`Theme: ${getLabel()}`}
      className="gap-2"
      disabled={!mounted} 
    >
      {getIcon()}
      <span className="hidden sm:inline">{getLabel()}</span>
    </Button>
  )
}