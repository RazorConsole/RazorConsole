import { ChevronRight } from "lucide-react"
import { Button } from "@/components/ui/Button"
import type React from "react"
import { cn } from "@/lib/utils"

interface Props {
  className?: string
  setMobileSidebarOpen: (open: boolean) => void
}

const MobileNavOpenButton: React.FC<Props> = ({ className, setMobileSidebarOpen }) => {
  return (
    <Button
      className={cn(
        className,
        "fixed bottom-6 left-6 z-50 flex h-12 w-12 items-center justify-center rounded-full bg-blue-600 p-0 text-white shadow-lg hover:bg-blue-700"
      )}
      onClick={() => setMobileSidebarOpen(true)}
    >
      <ChevronRight className="h-6 w-6" />
    </Button>
  )
}

export default MobileNavOpenButton
