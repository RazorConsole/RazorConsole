import { Link, Outlet } from "react-router-dom"
import { Github, Menu, X } from "lucide-react"
import { Button } from "@/components/ui/button"
import { ThemeToggle } from "@/components/ThemeToggle"
import { useState } from "react"
import { useGitHubStars } from "@/hooks/useGitHubStars"

export default function Layout() {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)
  const { stars } = useGitHubStars('RazorConsole', 'RazorConsole')

  return (
    <div className="min-h-screen flex flex-col">
      {/* Header */}
      <header className="sticky top-0 z-50 w-full border-b border-violet-500/80 bg-white/95 backdrop-blur supports-[backdrop-filter]:bg-white/60 dark:bg-slate-950/95 dark:supports-[backdrop-filter]:bg-slate-950/60">
        <div className="container mx-auto px-4">
          <div className="flex h-16 items-center justify-between">
            <div className="flex items-center gap-8">
              <Link to="/" className="flex items-center space-x-2">
                <span className="font-bold text-xl bg-gradient-to-r from-blue-500/80 to-violet-500/80 dark:from-blue-600 dark:to-violet-600 bg-clip-text text-transparent">
                  RazorConsole
                </span>
              </Link>
              {/* Desktop Navigation */}
              <nav className="hidden md:flex gap-6">
                <Link 
                  to="/" 
                  className="text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50 transition-colors"
                >
                  Home
                </Link>
                <Link 
                  to="/docs/quick-start" 
                  className="text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50 transition-colors"
                >
                  Docs
                </Link>
                <Link 
                  to="/api" 
                  className="text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50 transition-colors"
                >
                  API Reference
                </Link>
                <Link 
                  to="/components" 
                  className="text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50 transition-colors"
                >
                  Components
                </Link>
                <Link 
                  to="/collaborators" 
                  className="text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50 transition-colors"
                >
                  Collaborators
                </Link>
                <Link 
                  to="/showcase" 
                  className="text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50 transition-colors"
                >
                  Showcase
                </Link>
              </nav>
            </div>

            <div className="flex items-center gap-4">
              <ThemeToggle />
              
              <a 
                href="https://github.com/RazorConsole/RazorConsole" 
                target="_blank" 
                rel="noopener noreferrer"
                className="hidden md:block"
              >
                <Button 
                  variant="ghost" 
                  size="sm" 
                  className="gap-1.5 hover:cursor-pointer"
                  aria-label={stars !== null ? `GitHub - ${stars} stars` : "View on GitHub"}
                >
                  <Github className="w-5 h-5" />
                  {stars !== null && (
                    <span className="text-sm font-medium">
                      {stars >= 1000 
                        ? `${(stars / 1000).toFixed(1).replace(/\.0$/, '')}k` 
                        : stars.toLocaleString()}
                    </span>
                  )}
                </Button>
              </a>

              {/* Mobile menu button */}
              <button 
                className="md:hidden"
                onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
              >
                {mobileMenuOpen ? <X className="w-6 h-6" /> : <Menu className="w-6 h-6" />}
              </button>
            </div>
          </div>

          {/* Mobile Navigation */}
          {mobileMenuOpen && (
            <nav className="md:hidden pb-4 space-y-2">
              <Link 
                to="/" 
                className="block py-2 text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50"
                onClick={() => setMobileMenuOpen(false)}
              >
                Home
              </Link>
              <Link 
                to="/docs#quick-start" 
                className="block py-2 text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50"
                onClick={() => setMobileMenuOpen(false)}
              >
                Docs
              </Link>
              <Link 
                to="/api" 
                className="block py-2 text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50"
                onClick={() => setMobileMenuOpen(false)}
              >
                API Reference
              </Link>
              <Link 
                to="/components" 
                className="block py-2 text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50"
                onClick={() => setMobileMenuOpen(false)}
              >
                Components
              </Link>
              <Link 
                to="/collaborators" 
                className="block py-2 text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50"
                onClick={() => setMobileMenuOpen(false)}
              >
                Collaborators
              </Link>
              <Link 
                to="/showcase" 
                className="block py-2 text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50"
                onClick={() => setMobileMenuOpen(false)}
              >
                Showcase
              </Link>
              <a 
                href="https://github.com/RazorConsole/RazorConsole" 
                target="_blank" 
                rel="noopener noreferrer"
                className="block py-2 text-sm font-medium text-slate-600 hover:text-slate-900 dark:text-slate-300 dark:hover:text-slate-50"
              >
                GitHub
              </a>
            </nav>
          )}
        </div>
      </header>

      {/* Main Content */}
      <main className="flex-1">
        <Outlet />
      </main>

      {/* Footer */}
      <footer className="border-t py-8 bg-slate-50 dark:bg-slate-900">
        <div className="container mx-auto px-4">
          <div className="grid grid-cols-1 md:grid-cols-3 gap-8">
            <div>
              <h3 className="font-semibold mb-4">RazorConsole</h3>
              <p className="text-sm text-slate-600 dark:text-slate-400">
                Build rich, interactive console applications using Razor and Spectre.Console
              </p>
            </div>
            <div>
              <h3 className="font-semibold mb-4">Resources</h3>
              <ul className="space-y-2 text-sm">
                <li>
                  <a href="https://github.com/RazorConsole/RazorConsole" 
                     target="_blank" 
                     rel="noopener noreferrer"
                     className="text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-50">
                    GitHub Repository
                  </a>
                </li>
                <li>
                  <a href="https://www.nuget.org/packages/RazorConsole.Core" 
                     target="_blank" 
                     rel="noopener noreferrer"
                     className="text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-50">
                    NuGet Package
                  </a>
                </li>
                <li>
                  <a href="https://discord.gg/DphHAnJxCM" 
                     target="_blank" 
                     rel="noopener noreferrer"
                     className="text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-50">
                    Discord Community
                  </a>
                </li>
                <li>
                  <Link to="/collaborators" className="text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-50">
                    Collaborators
                  </Link>
                </li>
                <li>
                  <Link to="/showcase" className="text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-50">
                    Showcase
                  </Link>
                </li>
              </ul>
            </div>
            <div>
              <h3 className="font-semibold mb-4">Documentation</h3>
              <ul className="space-y-2 text-sm">
                <li>
                  <Link to="/docs" className="text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-50">
                    Docs Overview
                  </Link>
                </li>
                <li>
                  <Link to="/api" className="text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-50">
                    API Reference
                  </Link>
                </li>
                <li>
                  <Link to="/docs/quick-start" className="text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-50">
                    Quick Start Guide
                  </Link>
                </li>
                <li>
                  <Link to="/docs/built-in-components" className="text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-50">
                    Built-in Components
                  </Link>
                </li>
                <li>
                  <Link to="/docs/hot-reload" className="text-slate-600 hover:text-slate-900 dark:text-slate-400 dark:hover:text-slate-50">
                    Advanced Topics
                  </Link>
                </li>
              </ul>
            </div>
          </div>
          <div className="mt-8 pt-8 border-t text-center text-sm text-slate-600 dark:text-slate-400">
            <p>© 2024 RazorConsole. Licensed under MIT License.</p>
          </div>
        </div>
      </footer>
    </div>
  )
}
