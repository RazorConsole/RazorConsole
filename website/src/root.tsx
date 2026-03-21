import {
  Links,
  Meta,
  Outlet,
  Scripts,
  ScrollRestoration,
} from "react-router";
import "./index.css";
import { useThemeEffect } from "./hooks/useThemeEffect";
import { initHighlighter } from "./components/ui/CodeBlock";


export async function loader() {
  if (typeof window === "undefined") {
    await initHighlighter();
  }
}
export default function Root() {
  useThemeEffect();

  return (
    <html lang="en" suppressHydrationWarning>
      <head>
        <script
          dangerouslySetInnerHTML={{
            __html: `
              (function () {
                try {
                  const theme = localStorage.getItem('theme')
                  const root = document.documentElement
                  if (theme === 'dark') {
                    root.classList.add('dark')
                  } else if (theme === 'light') {
                    root.classList.remove('dark')
                  } else if (window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches) {
                    root.classList.add('dark')
                  }
                } catch (e) {}
              })()
            `,
          }}
        />
        <link rel="icon" type="image/svg+xml" href="/razorconsole-icon.svg" />
        <meta charSet="UTF-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        <link rel="preconnect" href="https://api.github.com" crossOrigin="anonymous" />
        <link rel="dns-prefetch" href="https://api.github.com" />
        <Meta />
        <Links />
      </head>
      <body id="root">
        <Outlet />

        <ScrollRestoration />
        <Scripts />

        {/* GitHub Pages SPA redirect script */}
        <script
          type="text/javascript"
          dangerouslySetInnerHTML={{
            __html: `
              (function (l) {
                if (l.search[1] === "/") {
                  var decoded = l.search
                    .slice(1)
                    .split("&")
                    .map(function (s) { return s.replace(/~and~/g, "&"); })
                    .join("?");
                  var finalUrl = l.pathname.slice(0, -1) + decoded + l.hash;
                  console.log("Final replaced location:", {
                    original: l.href,
                    decoded: decoded,
                    finalUrl: finalUrl,
                  });
                  window.history.replaceState(null, null, finalUrl);
                }
              })(window.location);
            `,
          }}
        />
      </body>
    </html>
  );
}
