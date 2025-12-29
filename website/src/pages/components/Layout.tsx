import { Outlet } from "react-router-dom"
import { components } from "@/data/components"
import { ResponsiveSidebar } from "@/components/ui/ResponsiveSidebar"
import Sidebar from "@/components/components/Sidebar"
import type { Category } from "@/types/components/category"
import type { ComponentInfo } from "@/types/components/componentInfo"

export default function ComponentsLayout() {
  const categories: Category[] = ["Layout", "Input", "Display", "Utilities"]
  const groupedComponents = categories.reduce(
    (acc, category) => {
      acc[category] = components.filter((c) => c.category === category)
      return acc
    },
    {} as Record<string, ComponentInfo[]>
  )

  return (
    <div className="container mx-auto px-4 py-8 md:py-12 lg:py-16">
      <div className="flex flex-col md:block">
        <ResponsiveSidebar breakpoint="lg" className="w-72 px-6 py-6">
          <Sidebar groupedComponents={groupedComponents} categories={categories} />
        </ResponsiveSidebar>

        <main className="relative min-w-0 flex-1 lg:ml-72">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
