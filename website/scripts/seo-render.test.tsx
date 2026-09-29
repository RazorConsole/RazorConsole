import assert from "node:assert/strict"
import * as React from "react"
import { test } from "node:test"
import { renderToStaticMarkup } from "react-dom/server"
import { MemoryRouter } from "react-router"
import { Link, NavLink } from "../src/components/ui/SiteLink"

test("SSR links retain the router base, queries, anchors and files", () => {
  const html = renderToStaticMarkup(React.createElement(
    MemoryRouter,
    { basename: "/RazorConsole", initialEntries: ["/RazorConsole/"] },
    <Link to="/components/table?view=all#parameters">Table</Link>,
    <Link to={{ pathname: "/docs/tutorial/hello-world", hash: "#setup" }}>Tutorial</Link>,
    <Link to="/raw/guide.md">Raw</Link>,
  ))
  assert.ok(html.includes('href="/RazorConsole/components/table/?view=all#parameters"'))
  assert.ok(html.includes('href="/RazorConsole/docs/tutorial/hello-world/#setup"'))
  assert.ok(html.includes('href="/RazorConsole/raw/guide.md"'))
})

test("canonical and legacy slashless routes keep active navigation and aria-current", () => {
  for (const pathname of ["/components/table", "/components/table/"]) {
    const html = renderToStaticMarkup(React.createElement(
      MemoryRouter,
      { initialEntries: [pathname] },
      <NavLink to="/components/table" end className={({ isActive }) => isActive ? "active" : "inactive"}>
        {({ isActive }) => isActive ? "Current component" : "Other component"}
      </NavLink>,
    ))
    assert.ok(html.includes('href="/components/table/"'), html)
    assert.ok(html.includes('aria-current="page"'), html)
    assert.ok(html.includes('class="active"'), html)
    assert.ok(html.includes("Current component"), html)
  }
})
