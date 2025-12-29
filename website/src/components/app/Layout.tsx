import { Outlet } from "react-router-dom"
import { Header } from "@/components/app/Header"
import { Footer } from "@/components/app/Footer"

export default function Layout() {
  return (
    <div className="flex min-h-screen flex-col">
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
