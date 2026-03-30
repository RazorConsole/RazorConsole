import HeroSection from "@/components/home/HeroSection"
import FeaturesGrid from "@/components/home/FeaturesGrid"
import QuickStartSection from "@/components/home/QuickStartSection"
import AdvancedTopicsSection from "@/components/home/AdvancedTopicsSection"
import type { MetaFunction } from "react-router";

export const meta: MetaFunction = () => {
  return [
    { title: "RazorConsole - Build Rich Console Apps with Razor" },
    { name: "description", content: "The modern framework for building interactive C# console applications using familiar Razor syntax and Spectre.Console components." },
    { property: "og:title", content: "RazorConsole - Rich TUI Framework" },
    { property: "og:description", content: "Build interactive CLI apps with C# and Razor." },
  ];
};
export default function Home() {
  return (
    <div className="min-h-screen bg-linear-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="container mx-auto px-4 py-16">
        <HeroSection />

        <FeaturesGrid />

        <QuickStartSection />

        <AdvancedTopicsSection />
      </div>
    </div>
  )
}
