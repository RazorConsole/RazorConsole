import { Link, useLocation } from "react-router-dom"
import { Github, Package, MessageCircle, Heart, Bug, BookOpen, Code, Gem } from "lucide-react"

export function Footer() {
  const location = useLocation()

  const isDocs =
    location.pathname.startsWith("/docs") ||
    location.pathname.startsWith("/api") ||
    location.pathname.startsWith("/components")

  const containerClasses = isDocs
    ? "w-full px-6 max-w-7xl mx-auto"
    : "container mx-auto px-4 md:px-6"

  const FooterHeader = ({ children }: { children: React.ReactNode }) => (
    <h3 className="mb-3 text-sm font-semibold tracking-wider text-slate-900 uppercase dark:text-slate-100">
      {children}
    </h3>
  )

  const FooterLink = ({ href, to, children, icon: Icon }: any) => {
    const classes =
      "group flex items-center gap-2 text-sm text-slate-600 transition-colors hover:text-blue-600 dark:text-slate-400 dark:hover:text-blue-400"

    if (to) {
      return (
        <Link to={to} className={classes}>
          {Icon && (
            <Icon className="h-3.5 w-3.5 text-slate-400 transition-colors group-hover:text-blue-600 dark:text-slate-500 dark:group-hover:text-blue-400" />
          )}
          {children}
        </Link>
      )
    }
    return (
      <a href={href} target="_blank" rel="noopener noreferrer" className={classes}>
        {Icon && (
          <Icon className="h-3.5 w-3.5 text-slate-400 transition-colors group-hover:text-blue-600 dark:text-slate-500 dark:group-hover:text-blue-400" />
        )}
        {children}
      </a>
    )
  }

  return (
    <footer className="border-t border-slate-200 bg-slate-50 py-12 dark:border-slate-800 dark:bg-slate-900/30">
      <div className={containerClasses}>
        <div className="grid grid-cols-1 gap-12 md:grid-cols-4 lg:gap-8">
          {/* Project Links */}
          <div>
            <FooterHeader>Project</FooterHeader>
            <ul className="space-y-3">
              <li>
                <FooterLink href="https://github.com/RazorConsole/RazorConsole" icon={Github}>
                  GitHub
                </FooterLink>
              </li>
              <li>
                <FooterLink href="https://www.nuget.org/packages/RazorConsole.Core" icon={Package}>
                  NuGet
                </FooterLink>
              </li>
              <li>
                <FooterLink to="/showcase" icon={Gem}>
                  Showcase
                </FooterLink>
              </li>
              <li>
                <FooterLink to="/collaborators" icon={Heart}>
                  Collaborators
                </FooterLink>
              </li>
            </ul>
          </div>

          {/* Resources Links */}
          <div>
            <FooterHeader>Resources</FooterHeader>
            <ul className="space-y-3">
              <li>
                <FooterLink to="/docs/quick-start" icon={BookOpen}>
                  Documentation
                </FooterLink>
              </li>
              <li>
                <FooterLink to="/api" icon={Code}>
                  API Reference
                </FooterLink>
              </li>
              <li>
                <FooterLink href="https://discord.gg/DphHAnJxCM" icon={MessageCircle}>
                  Discord Community
                </FooterLink>
              </li>
              <li>
                <FooterLink href="https://github.com/RazorConsole/RazorConsole/issues" icon={Bug}>
                  Report Issue
                </FooterLink>
              </li>
            </ul>
          </div>
        </div>

        {/* Bottom Bar */}
        <div className="mt-12 flex flex-col items-center justify-between gap-4 border-t border-slate-200 pt-8 text-sm text-slate-500 md:flex-row dark:border-slate-800 dark:text-slate-500">
          <p>© {new Date().getFullYear()} RazorConsole. Licensed under MIT.</p>

          <div className="flex gap-6">
            <a
              href="https://github.com/RazorConsole/RazorConsole/blob/main/LICENSE"
              target="_blank"
              rel="noreferrer"
              className="transition-colors hover:text-slate-900 dark:hover:text-slate-200"
            >
              License
            </a>
            <a
              href="https://github.com/RazorConsole/RazorConsole"
              target="_blank"
              rel="noreferrer"
              className="transition-colors hover:text-slate-900 dark:hover:text-slate-200"
            >
              Source Code
            </a>
          </div>
        </div>
      </div>
    </footer>
  )
}
