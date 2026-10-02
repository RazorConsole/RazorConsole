import HeroSection from "@/components/home/HeroSection"
import GallerySection from "@/components/home/GallerySection"
import QuickStartSection from "@/components/home/QuickStartSection"
import FaqSection from "@/components/home/FaqSection"
import type { MetaFunction } from "react-router"
import { getPageUrl } from "@/lib/utils"

export const meta: MetaFunction = ({ matches, location }) => {
  const rootMeta = matches.find((m) => m.id === "root")?.meta || []
  const pageUrl = getPageUrl(location.pathname)

  return [
    ...rootMeta,

    { title: "RazorConsole: C# Terminal UI Framework with Razor" },
    { property: "og:url", content: pageUrl },
    {
      name: "description",
      content:
        "Build .NET terminal UIs with reusable Razor components, built-in mouse and keyboard events, and experimental NativeAOT support for native distribution.",
    },
    { property: "og:title", content: "RazorConsole: C# Terminal UI Framework with Razor" },
    { property: "og:description", content: "Build .NET terminal UIs with reusable Razor components, built-in mouse and keyboard events, and experimental NativeAOT support for native distribution." },
  ]
}
export default function Home() {
  return (
    <div className="min-h-screen bg-linear-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="container mx-auto px-4 py-16">
        <HeroSection />

        <QuickStartSection />

        <GallerySection />

        <FaqSection />
      </div>
    </div>
  )
}
