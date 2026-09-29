import { createPortal } from "react-dom"
import { useEffect, useRef, useState } from "react"
import { ChevronDown, Github, Menu, X } from "lucide-react"
import { useLocation } from "react-router-dom"
import { Link, NavLink } from "@/components/ui/SiteLink"
import { ThemeToggle } from "@/components/app/ThemeToggle"
import { Button } from "@/components/ui/Button"
import { useGitHubStars } from "@/hooks/useGitHubStars"
import { cn } from "@/lib/utils"
import iconUrl from "@/assets/icons/icon.svg"

const docsLinks = [
  { to: "/docs/tutorial/hello-world", label: "Tutorial", description: "Build your first app" },
  { to: "/release-notes", label: "Release Notes", description: "See what changed" },
  { to: "/api", label: "API Reference", description: "Browse the .NET API" },
  { to: "/components", label: "Components", description: "Explore built-in UI" },
]

export function Header() {
  const { stars } = useGitHubStars("RazorConsole", "RazorConsole")
  const [isScrolled, setIsScrolled] = useState(false)
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)
  const [desktopDocsOpen, setDesktopDocsOpen] = useState(false)
  const [mobileDocsOpen, setMobileDocsOpen] = useState(false)
  const [isMounted, setIsMounted] = useState(false)
  const docsDropdownRef = useRef<HTMLDivElement>(null)
  const docsTriggerRef = useRef<HTMLButtonElement>(null)
  const location = useLocation()

  useEffect(() => {
    setIsMounted(true)
  }, [])

  useEffect(() => {
    setMobileMenuOpen(false)
    setDesktopDocsOpen(false)
  }, [location.pathname])

  useEffect(() => {
    const handleScroll = () => setIsScrolled(window.scrollY > 10)
    handleScroll()
    window.addEventListener("scroll", handleScroll)
    return () => window.removeEventListener("scroll", handleScroll)
  }, [])

  useEffect(() => {
    if (!desktopDocsOpen) return

    const handlePointerDown = (event: PointerEvent) => {
      if (!docsDropdownRef.current?.contains(event.target as Node)) setDesktopDocsOpen(false)
    }
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") {
        setDesktopDocsOpen(false)
        docsTriggerRef.current?.focus()
      }
    }

    document.addEventListener("pointerdown", handlePointerDown)
    document.addEventListener("keydown", handleKeyDown)
    return () => {
      document.removeEventListener("pointerdown", handlePointerDown)
      document.removeEventListener("keydown", handleKeyDown)
    }
  }, [desktopDocsOpen])

  const docsActive =
    location.pathname.startsWith("/docs") ||
    location.pathname.startsWith("/release-notes") ||
    location.pathname.startsWith("/api") ||
    location.pathname.startsWith("/components")

  const isContentPage = docsActive || location.pathname.startsWith("/blog")

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

  const mobileLinkClasses = (to: string) =>
    cn(
      "block rounded-md px-4 py-2.5 text-sm font-medium transition-colors",
      to === "/"
        ? location.pathname === "/"
        : location.pathname.startsWith(to)
          ? "bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400"
          : "text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-slate-800"
    )

  const mobileMenu = (
    <div
      className={cn(
        "fixed inset-0 z-[60] transition-all duration-300 ease-in-out lg:hidden",
        mobileMenuOpen ? "visible" : "pointer-events-none invisible"
      )}
    >
      <div
        className={cn(
          "absolute inset-0 bg-black/80 backdrop-blur-sm transition-opacity duration-300",
          mobileMenuOpen ? "opacity-100" : "opacity-0"
        )}
        onClick={() => setMobileMenuOpen(false)}
      />

      <div
        className={cn(
          "absolute inset-y-0 left-0 flex w-3/4 max-w-xs flex-col gap-8 overflow-y-auto border-r border-slate-200 bg-white p-6 shadow-2xl transition-transform duration-300 ease-in-out dark:border-slate-800 dark:bg-slate-950",
          mobileMenuOpen ? "translate-x-0" : "-translate-x-full"
        )}
      >
        <div className="flex items-center justify-between">
          <span className="bg-gradient-to-r from-blue-500/80 to-violet-500/80 bg-clip-text text-xl font-bold text-transparent dark:from-blue-600 dark:to-violet-600">
            RazorConsole
          </span>
          <Button
            variant="ghost"
            aria-label="Close navigation panel"
            size="icon"
            className="rounded-full"
            onClick={() => setMobileMenuOpen(false)}
          >
            <X className="h-5 w-5 text-slate-900 dark:text-slate-50" />
          </Button>
        </div>

        <nav className="flex flex-col gap-2" aria-label="Mobile navigation">
          <Link to="/" className={mobileLinkClasses("/")}>
            Home
          </Link>

          <div>
            <button
              type="button"
              aria-expanded={mobileDocsOpen}
              aria-controls="mobile-docs-navigation"
              onClick={() => setMobileDocsOpen((open) => !open)}
              className={cn(
                "flex w-full items-center justify-between rounded-md px-4 py-2.5 text-left text-sm font-medium transition-colors",
                docsActive
                  ? "bg-blue-50 text-blue-700 dark:bg-blue-500/10 dark:text-blue-400"
                  : "text-slate-700 hover:bg-slate-100 dark:text-slate-200 dark:hover:bg-slate-800"
              )}
            >
              Docs
              <ChevronDown
                className={cn("h-4 w-4 transition-transform", mobileDocsOpen && "rotate-180")}
                aria-hidden="true"
              />
            </button>
            {mobileDocsOpen && (
              <div
                id="mobile-docs-navigation"
                className="mt-1 space-y-1 border-l border-slate-200 pl-3 dark:border-slate-800"
              >
                {docsLinks.map((item) => (
                  <Link key={item.to} to={item.to} className={mobileLinkClasses(item.to)}>
                    {item.label}
                  </Link>
                ))}
              </div>
            )}
          </div>

          {[
            { to: "/blog", label: "Blog" },
            { to: "/gallery", label: "Gallery" },
            { to: "/showcase", label: "Showcase" },
            { to: "/collaborators", label: "Collaborators" },
          ].map((item) => (
            <Link key={item.to} to={item.to} className={mobileLinkClasses(item.to)}>
              {item.label}
            </Link>
          ))}
        </nav>

        <div className="mt-auto border-t border-slate-100 pt-6 dark:border-slate-800">
          <a
            href="https://github.com/RazorConsole/RazorConsole"
            target="_blank"
            rel="noopener noreferrer"
            aria-label="View RazorConsole on GitHub"
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
        isScrolled || isContentPage
          ? "border-slate-200 bg-white/80 backdrop-blur-md dark:border-slate-800 dark:bg-slate-950/80"
          : "bg-transparent"
      )}
    >
      <div className="mx-auto flex h-16 w-full items-center justify-between px-4 md:px-6">
        <div className="flex items-center gap-8">
          <Link to="/" className="group flex items-center space-x-2" aria-label="RazorConsole Home">
            <div
              className="animate-shimmer min-h-[45px] min-w-[45px] bg-gradient-to-br from-blue-600 via-purple-600 to-purple-800 transition-transform dark:from-cyan-400 dark:via-purple-500 dark:to-purple-700"
              style={{
                mask: `url("${iconUrl}") no-repeat center / contain`,
                WebkitMask: `url("${iconUrl}") no-repeat center / contain`,
              }}
            />
          </Link>

          <nav className="hidden items-center gap-6 lg:flex" aria-label="Primary navigation">
            <NavItem to="/">Home</NavItem>

            <div ref={docsDropdownRef} className="relative">
              <button
                ref={docsTriggerRef}
                type="button"
                aria-expanded={desktopDocsOpen}
                aria-controls="desktop-docs-navigation"
                onClick={() => setDesktopDocsOpen((open) => !open)}
                className={cn(
                  "relative flex items-center gap-1 py-1.5 text-sm font-medium transition-colors hover:text-violet-600 focus-visible:outline-2 focus-visible:outline-offset-4 focus-visible:outline-violet-600 dark:hover:text-violet-400",
                  docsActive ? "text-foreground font-semibold" : "text-muted-foreground"
                )}
              >
                Docs
                <ChevronDown
                  className={cn(
                    "h-3.5 w-3.5 transition-transform",
                    desktopDocsOpen && "rotate-180"
                  )}
                  aria-hidden="true"
                />
                <span
                  className={cn(
                    "absolute bottom-0 left-0 h-[2px] w-full bg-violet-600 transition-transform dark:bg-violet-400",
                    docsActive ? "scale-x-100" : "scale-x-0"
                  )}
                />
              </button>

              {desktopDocsOpen && (
                <div
                  id="desktop-docs-navigation"
                  className="absolute top-full left-0 mt-3 w-64 rounded-xl border border-slate-200 bg-white p-2 shadow-xl dark:border-slate-800 dark:bg-slate-950"
                >
                  {docsLinks.map((item) => (
                    <Link
                      key={item.to}
                      to={item.to}
                      className="block rounded-lg px-3 py-2.5 transition-colors hover:bg-slate-100 focus-visible:bg-slate-100 focus-visible:outline-none dark:hover:bg-slate-900 dark:focus-visible:bg-slate-900"
                    >
                      <span className="block text-sm font-semibold text-slate-900 dark:text-white">
                        {item.label}
                      </span>
                      <span className="mt-0.5 block text-xs text-slate-500 dark:text-slate-400">
                        {item.description}
                      </span>
                    </Link>
                  ))}
                </div>
              )}
            </div>

            <NavItem to="/blog">Blog</NavItem>
            <NavItem to="/gallery">Gallery</NavItem>
            <NavItem to="/showcase">Showcase</NavItem>
            <NavItem to="/collaborators">Collaborators</NavItem>
          </nav>
        </div>

        <div className="flex items-center gap-3">
          <a
            href="https://github.com/RazorConsole/RazorConsole"
            target="_blank"
            rel="noopener noreferrer"
            aria-label={`View GitHub repository, currently has ${stars ?? 0} stars`}
            className="hidden items-center gap-2 rounded-full border border-slate-200 bg-slate-50/50 px-3 py-1.5 text-xs font-medium text-slate-700 transition-colors hover:bg-slate-100 hover:text-slate-900 sm:flex dark:border-slate-800 dark:bg-slate-900/50 dark:text-slate-300 dark:hover:bg-slate-800 dark:hover:text-slate-50"
          >
            <Github className="h-3.5 w-3.5" />
            <span>Stars</span>
            <div className="mx-0.5 h-3 w-px bg-slate-300 dark:bg-slate-700" />
            <span className="font-mono tabular-nums">
              {stars !== null ? (stars >= 1000 ? `${(stars / 1000).toFixed(1)}k` : stars) : "..."}
            </span>
          </a>

          <div className="mx-1 hidden h-4 w-px bg-slate-200 sm:block dark:bg-slate-800" />
          <ThemeToggle />
          <Button
            variant="ghost"
            size="icon"
            className="rounded-full lg:hidden"
            onClick={() => setMobileMenuOpen(true)}
            aria-label="Open navigation menu"
          >
            <Menu className="h-6 w-6" />
          </Button>
        </div>
      </div>

      {isMounted && createPortal(mobileMenu, document.body)}
    </header>
  )
}
