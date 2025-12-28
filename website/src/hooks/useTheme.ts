import { create } from "zustand"

type Theme = "light" | "dark" | "system"

type ThemeStore = {
  theme: Theme
  setTheme: (theme: Theme) => void
}

export const useTheme = create<ThemeStore>((set) => ({
  theme: (localStorage.getItem("theme") as Theme) || "system",
  setTheme: (theme: Theme) => {
    localStorage.setItem("theme", theme)
    set({ theme })
  },
}))
