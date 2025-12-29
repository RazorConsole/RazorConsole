import { Link, NavLink, useLocation } from "react-router-dom"
import { Github, X, Menu } from "lucide-react"
import { Button } from "@/components/ui/Button"
import { ThemeToggle } from "@/components/app/ThemeToggle"
import { useGitHubStars } from "@/hooks/useGitHubStars"
import { cn } from "@/lib/utils"
import { useState, useEffect } from "react"
import iconUrl from "@/assets/icons/icon.svg"
import { createPortal } from "react-dom"

export function Header() {
  const { stars } = useGitHubStars("RazorConsole", "RazorConsole")
  const [isScrolled, setIsScrolled] = useState(false)
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)
  const location = useLocation()

  useEffect(() => {
    setMobileMenuOpen(false)
  }, [location.pathname])

  useEffect(() => {
    const handleScroll = () => {
      setIsScrolled(window.scrollY > 10)
    }
    window.addEventListener("scroll", handleScroll)
    return () => window.removeEventListener("scroll", handleScroll)
  }, [])

  const NavItem = ({ to, children }: { to: string; children: React.ReactNode }) => (
    <NavLink
      to={to}
      className={({ isActive }) =>
        cn(
          "relative py-1.5 text-sm font-medium transition-colors hover:text-violet-600 dark:hover:text-violet-400",
          isActive ? "text-foreground font-semibold" : "text-muted-foreground"
        )
      }
    >
      {({ isActive }) => (
        <>
          {children}
          <span
            className={cn(
              "absolute bottom-0 left-0 h-[2px] w-full origin-left bg-violet-600 transition-transform duration-300 ease-out dark:bg-violet-400",
              isActive ? "scale-x-100" : "scale-x-0"
            )}
          />
        </>
      )}
    </NavLink>
  )

  const isDocs =
    location.pathname.startsWith("/docs") ||
    location.pathname.startsWith("/api") ||
    location.pathname.startsWith("/components")

  const mobileMenu = (
    <div
      className={cn(
        "fixed inset-0 z-[60] transition-all duration-300 ease-in-out md:hidden",
        mobileMenuOpen ? "visible" : "pointer-events-none invisible"
      )}
    >
      {/* Backdrop */}
      <div
        className={cn(
          "absolute inset-0 bg-black/80 backdrop-blur-sm transition-opacity duration-300",
          mobileMenuOpen ? "opacity-100" : "opacity-0"
        )}
        onClick={() => setMobileMenuOpen(false)}
      />

      {/* Drawer Panel */}
      <div
        className={cn(
          "absolute inset-y-0 left-0 flex w-3/4 max-w-xs flex-col gap-8 border-r border-slate-200 bg-white p-6 shadow-2xl transition-transform duration-300 ease-in-out dark:border-slate-800 dark:bg-slate-950",
          "overflow-y-auto [&::-webkit-scrollbar]:w-2 [&::-webkit-scrollbar-thumb]:rounded-full [&::-webkit-scrollbar-thumb]:bg-slate-200 hover:[&::-webkit-scrollbar-thumb]:bg-slate-300 dark:[&::-webkit-scrollbar-thumb]:bg-slate-800 dark:hover:[&::-webkit-scrollbar-thumb]:bg-slate-700 [&::-webkit-scrollbar-track]:bg-transparent",
          mobileMenuOpen ? "translate-x-0" : "-translate-x-full"
        )}
      >
        <div className="flex items-center justify-between">
          <span className="bg-gradient-to-r from-blue-500/80 to-violet-500/80 bg-clip-text text-xl font-bold text-transparent dark:from-blue-600 dark:to-violet-600">
            RazorConsole
          </span>
          <Button
            variant="ghost"
            size="icon"
            className="rounded-full"
            onClick={() => setMobileMenuOpen(false)}
          >
            <X className="h-5 w-5 text-slate-900 dark:text-slate-50" />
          </Button>
        </div>

        <nav className="flex flex-col gap-2">
          {[
            { to: "/", label: "Home" },
            { to: "/docs/quick-start", label: "Docs" },
            { to: "/api", label: "API Reference" },
            { to: "/components", label: "Components" },
            { to: "/collaborators", label: "Collaborators" },
            { to: "/showcase", label: "Showcase" },
          ].map((item) => (
            <Link
              key={item.to}
              to={item.to}
              className={cn(
                "block rounded-md px-4 py-2.5 text-sm font-medium transition-colors",
                location.pathname === item.to
                  ? "bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400"
                  : "text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-slate-800"
              )}
            >
              {item.label}
            </Link>
          ))}
        </nav>

        <div className="mt-auto border-t border-slate-100 pt-6 dark:border-slate-800">
          <a
            href="https://github.com/RazorConsole/RazorConsole"
            target="_blank"
            rel="noopener noreferrer"
            className="flex items-center gap-3 px-4 py-2 text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-50"
          >
            <Github className="h-5 w-5" />
            GitHub
          </a>
        </div>
      </div>
    </div>
  )

  return (
    <header
      className={cn(
        "sticky top-0 z-50 w-full border-b border-transparent",
        isScrolled || isDocs
          ? "border-slate-200 bg-white/80 backdrop-blur-md dark:border-slate-800 dark:bg-slate-950/80"
          : "bg-transparent"
      )}
    >
      <div className="mx-auto flex h-16 w-full items-center justify-between px-4 md:px-6">
        {/* Left Section */}
        <div className="flex items-center gap-8">
          <Link to="/" className="group flex items-center space-x-2">
            <div
              className="animate-shimmer min-h-[45px] min-w-[45px] bg-gradient-to-br from-blue-600 via-purple-600 to-purple-800 transition-transform dark:from-cyan-400 dark:via-purple-500 dark:to-purple-700"
              style={{
                mask: `url("${iconUrl}") no-repeat center / contain`,
                WebkitMask: `url("${iconUrl}") no-repeat center / contain`,
              }}
            />
          </Link>

          {/* Desktop Navigation */}
          <nav className="hidden items-center gap-6 md:flex">
            <NavItem to="/">Home</NavItem>
            <NavItem to="/docs/quick-start">Docs</NavItem>
            <NavItem to="/api">API</NavItem>
            <NavItem to="/components">Components</NavItem>
            <NavItem to="/showcase">Showcase</NavItem>
            <NavItem to="/collaborators">Collaborators</NavItem>
          </nav>
        </div>

        {/* Right Section */}
        <div className="flex items-center gap-3">
          {/* GitHub Star Pill Badge */}
          <a
            href="https://github.com/RazorConsole/RazorConsole"
            target="_blank"
            rel="noopener noreferrer"
            className="hidden items-center gap-2 rounded-full border border-slate-200 bg-slate-50/50 px-3 py-1.5 text-xs font-medium text-slate-700 transition-colors hover:bg-slate-100 hover:text-slate-900 sm:flex dark:border-slate-800 dark:bg-slate-900/50 dark:text-slate-300 dark:hover:bg-slate-800 dark:hover:text-slate-50"
          >
            <Github className="h-3.5 w-3.5" />
            <span>Stars</span>
            <div className="mx-0.5 h-3 w-[1px] bg-slate-300 dark:bg-slate-700" />
            <span className="font-mono tabular-nums">
              {stars !== null ? (stars >= 1000 ? `${(stars / 1000).toFixed(1)}k` : stars) : "..."}
            </span>
          </a>

          <div className="mx-1 hidden h-4 w-[1px] bg-slate-200 sm:block dark:bg-slate-800" />

          <ThemeToggle />

          {/* Mobile Menu Button */}
          <Button
            variant="ghost"
            size="icon"
            className="rounded-full md:hidden"
            onClick={() => setMobileMenuOpen(true)}
          >
            <Menu className="h-6 w-6" />
          </Button>
        </div>
      </div>

      {/* Mobile Navigation */}
      {createPortal(mobileMenu, document.body)}
    </header>
  )
}
