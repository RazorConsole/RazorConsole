import { Outlet, useLocation } from "react-router-dom"
import { cn } from "@/lib/utils"
import { Header } from "@/components/app/Header"
import { Footer } from "@/components/app/Footer"

export default function Layout() {
  const location = useLocation()
  const isDocs =
    location.pathname.startsWith("/docs") ||
    location.pathname.startsWith("/api") ||
    location.pathname.startsWith("/components")

  const layoutClasses = cn("min-h-screen flex flex-col", isDocs && "lg:pl-72")

  return (
    <div className={layoutClasses}>
      {/* Header */}
      <Header />

      {/* Main Content */}
      <main className="flex-1">
        <Outlet />
      </main>

      {/* Footer */}
      <Footer />
    </div>
  )
}
