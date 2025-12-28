import { useEffect } from "react"
import { useTheme } from "@/hooks/useTheme"

// Hook to apply theme changes to the DOM
export function useThemeEffect() {
  const theme = useTheme((state) => state.theme)

  useEffect(() => {
    const root = window.document.documentElement

    root.classList.remove("light", "dark")

    if (theme === "system") {
      const systemTheme = window.matchMedia("(prefers-color-scheme: dark)").matches
        ? "dark"
        : "light"

      root.classList.add(systemTheme)
      return
    }

    root.classList.add(theme)
  }, [theme])
}
