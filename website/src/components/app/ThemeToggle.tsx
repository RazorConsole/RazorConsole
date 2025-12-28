import { Moon, Sun, Monitor } from "lucide-react"
import { Button } from "@/components/ui/Button"
import { useTheme } from "@/hooks/useTheme"

export function ThemeToggle() {
  const theme = useTheme((state) => state.theme)
  const setTheme = useTheme((state) => state.setTheme)

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
    switch (theme) {
      case "light":
        return <Sun className="h-4 w-4" />
      case "dark":
        return <Moon className="h-4 w-4" />
      case "system":
        return <Monitor className="h-4 w-4" />
    }
  }

  const getLabel = () => {
    switch (theme) {
      case "light":
        return "Light"
      case "dark":
        return "Dark"
      case "system":
        return "System"
    }
  }

  return (
    <Button
      variant="ghost"
      size="sm"
      onClick={cycleTheme}
      title={`Theme: ${getLabel()}`}
      className="gap-2"
    >
      {getIcon()}
      <span className="hidden sm:inline">{getLabel()}</span>
    </Button>
  )
}
