import { useState, useEffect } from "react"
import { useLocation } from "react-router-dom"
import { X } from "lucide-react"
import { Button } from "@/components/ui/Button"
import { cn } from "@/lib/utils"
import MobileNavOpenButton from "@/components/ui/MobileNavOpenButton"

interface ResponsiveSidebarProps {
  children: React.ReactNode
  className?: string
  breakpoint?: "md" | "lg"
}

export function ResponsiveSidebar({
  children,
  className,
  breakpoint = "lg",
}: ResponsiveSidebarProps) {
  const [isOpen, setIsOpen] = useState(false)
  const location = useLocation()

  // Close sidebar on navigation
  useEffect(() => {
    setIsOpen(false)
  }, [location.pathname, location.hash])

  const desktopHiddenClass = breakpoint === "md" ? "md:block" : "lg:block"
  const mobileVisibleClass = breakpoint === "md" ? "md:hidden" : "lg:hidden"

  const scrollbarStyles = cn(
    // Firefox
    "[scrollbar-color:theme('colors.slate.300')_transparent]",
    "dark:[scrollbar-color:theme('colors.slate.700')_transparent]",

    // Other Browsers
    "[&::-webkit-scrollbar-thumb]:bg-slate-300",
    "dark:[&::-webkit-scrollbar-thumb]:bg-slate-700",
    "hover:[&::-webkit-scrollbar-thumb]:bg-slate-400",
    "dark:hover:[&::-webkit-scrollbar-thumb]:bg-slate-600",

    "[&::-webkit-scrollbar]:w-1",
    "[&::-webkit-scrollbar]:h-1",
    "[&::-webkit-scrollbar-track]:bg-transparent",
    "[&::-webkit-scrollbar-thumb]:rounded-full"
  )

  return (
    <>
      {/* Desktop Sidebar */}
      <aside
        className={cn(
          "fixed top-0 bottom-0 left-0 z-40 hidden overflow-y-auto border-r border-slate-200 bg-slate-50/50 backdrop-blur-xl dark:border-slate-800 dark:bg-slate-950/50",
          desktopHiddenClass,
          scrollbarStyles,
          className
        )}
      >
        {children}
      </aside>

      {/* Mobile Trigger Button */}
      <MobileNavOpenButton
        className={cn("hidden", mobileVisibleClass)}
        setMobileSidebarOpen={setIsOpen}
      />

      {/* Mobile Drawer Overlay */}
      <div
        className={cn(
          "fixed inset-0 z-[60] transition-all duration-300 ease-in-out",
          mobileVisibleClass,
          isOpen ? "visible" : "pointer-events-none invisible"
        )}
      >
        {/* Backdrop */}
        <div
          className={cn(
            "absolute inset-0 bg-black/80 backdrop-blur-sm transition-opacity duration-300",
            isOpen ? "opacity-100" : "opacity-0"
          )}
          onClick={() => setIsOpen(false)}
        />
        {/* Drawer Panel */}
        <div
          className={cn(
            "absolute inset-y-0 left-0 w-3/4 max-w-xs overflow-y-auto border-r border-slate-200 bg-white p-6 shadow-2xl transition-transform duration-300 ease-in-out dark:border-slate-800 dark:bg-slate-950",
            scrollbarStyles,
            isOpen ? "translate-x-0" : "-translate-x-full"
          )}
        >
          <div className="mb-4 flex justify-end">
            <Button
              variant="ghost"
              size="icon"
              className="rounded-full"
              onClick={() => setIsOpen(false)}
            >
              <X className="h-5 w-5" />
            </Button>
          </div>
          {/* Content Wrapper */}
          <div>{children}</div>
        </div>
      </div>
    </>
  )
}
