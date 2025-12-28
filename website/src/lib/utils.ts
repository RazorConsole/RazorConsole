import type { Category } from "@/types/components/category"
import { type ClassValue, clsx } from "clsx"
import { twMerge } from "tailwind-merge"

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs))
}

export function getCategoryBadgeColor(category: Category) {
  switch (category) {
    case "Layout":
      return "border-blue-200 bg-blue-50 text-blue-700 dark:border-blue-500/40 dark:bg-blue-500/10 dark:text-blue-300"
    case "Input":
      return "border-purple-200 bg-purple-50 text-purple-700 dark:border-purple-500/40 dark:bg-purple-500/10 dark:text-purple-300"
    case "Display":
      return "border-teal-200 bg-teal-50 text-teal-700 dark:border-teal-500/40 dark:bg-teal-500/10 dark:text-teal-300"
    case "Utilities":
      return "border-orange-200 bg-orange-50 text-orange-700 dark:border-orange-500/40 dark:bg-orange-500/10 dark:text-orange-300"
    default:
      return "border-slate-200 bg-slate-50 text-slate-700 dark:border-slate-500/40 dark:bg-slate-500/10 dark:text-slate-300"
  }
}
