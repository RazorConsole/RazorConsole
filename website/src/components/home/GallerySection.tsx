import { ArrowRight, Images } from "lucide-react"
import { Link } from "@/components/ui/SiteLink"
import { Button } from "@/components/ui/Button"
import { Card, CardContent } from "@/components/ui/Card"

export default function GallerySection() {
  return (
    <Card className="overflow-hidden border-violet-200 dark:border-violet-900/70">
      <CardContent className="flex flex-col gap-6 bg-gradient-to-br from-violet-50 via-white to-blue-50 p-6 sm:flex-row sm:items-center sm:justify-between sm:p-8 dark:from-violet-950/40 dark:via-slate-950 dark:to-blue-950/30">
        <div className="flex gap-4">
          <div className="mt-1 shrink-0 self-start rounded-xl bg-violet-100 p-3 text-violet-700 dark:bg-violet-500/10 dark:text-violet-300">
            <Images className="h-6 w-6" aria-hidden="true" />
          </div>
          <div>
            <h2 className="mb-2 text-2xl font-bold tracking-tight text-slate-950 dark:text-white">
              RazorConsole Gallery
            </h2>
            <p className="max-w-2xl text-slate-600 dark:text-slate-300">
              Explore official interactive RazorConsole applications, including the component
              workbench and Snake.
            </p>
          </div>
        </div>
        <Link to="/gallery">
          <Button variant="outline" className="w-full shrink-0 gap-2 sm:w-auto">
            Explore the Gallery
            <ArrowRight className="h-4 w-4" aria-hidden="true" />
          </Button>
        </Link>
      </CardContent>
    </Card>
  )
}
