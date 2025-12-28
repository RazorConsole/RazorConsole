import HeroSection from "@/components/home/HeroSection"
import FeaturesGrid from "@/components/home/FeaturesGrid"
import QuickStartSection from "@/components/home/QuickStartSection"
import AdvancedTopicsSection from "@/components/home/AdvancedTopicsSection"

export default function Home() {
  return (
    <div className="min-h-screen bg-gradient-to-b from-slate-50 to-white dark:from-slate-950 dark:to-slate-900">
      <div className="container mx-auto px-4 py-16">
        <HeroSection />

        <FeaturesGrid />

        <QuickStartSection />

        <AdvancedTopicsSection />
      </div>
    </div>
  )
}
