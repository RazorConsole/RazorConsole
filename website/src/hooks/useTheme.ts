import { create } from "zustand"

type Theme = "light" | "dark" | "system"

type ThemeStore = {
  theme: Theme
  setTheme: (theme: Theme) => void
}

const isBrowser = typeof window !== "undefined"

export const useTheme = create<ThemeStore>((set) => ({
  theme: isBrowser ? (localStorage.getItem("theme") as Theme) || "system" : "system",

  setTheme: (theme: Theme) => {
    if (isBrowser) {
      localStorage.setItem("theme", theme)
    }
    set({ theme })
  },
}))

export function useResolvedTheme() {
  const theme = useTheme((s) => s.theme)
  
  if (typeof window === "undefined") return "dark"

  if (theme === "system") {
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"
  }
  
  return theme as "dark" | "light"
}