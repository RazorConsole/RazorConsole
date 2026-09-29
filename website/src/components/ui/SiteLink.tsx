import * as React from "react"
import {
  Link as RouterLink,
  NavLink as RouterNavLink,
  type LinkProps,
  type NavLinkProps,
  type NavLinkRenderProps,
  type To,
  useLocation,
  useResolvedPath,
} from "react-router"
import { pagePath } from "../../lib/site-paths"

function normalizeTo(to: To): To {
  return typeof to === "string"
    ? pagePath(to)
    : { ...to, ...(to.pathname ? { pathname: pagePath(to.pathname) } : {}) }
}

export function Link({ to, ...props }: LinkProps) {
  return React.createElement(RouterLink, { to: normalizeTo(to), ...props })
}

export function NavLink({ to, className, style, children, caseSensitive, end, ...props }: NavLinkProps) {
  const normalized = normalizeTo(to)
  const resolved = useResolvedPath(normalized, { relative: props.relative })
  const location = useLocation()
  const equivalent = caseSensitive
    ? pagePath(location.pathname) === pagePath(resolved.pathname)
    : pagePath(location.pathname).toLowerCase() === pagePath(resolved.pathname).toLowerCase()
  // Legacy slashless URLs still need active styling and aria-current on canonical links.
  if (equivalent && !location.pathname.endsWith("/")) {
    const state: NavLinkRenderProps = { isActive: true, isPending: false, isTransitioning: false }
    return (
      <RouterLink
        {...props}
        to={normalized}
        aria-current={props["aria-current"] ?? "page"}
        className={typeof className === "function" ? className(state) : className}
        style={typeof style === "function" ? style(state) : style}
      >
        {typeof children === "function" ? children(state) : children}
      </RouterLink>
    )
  }

  return (
    <RouterNavLink
      {...props}
      to={normalized}
      caseSensitive={caseSensitive}
      end={end}
      className={className}
      style={style}
    >
      {children}
    </RouterNavLink>
  )
}
