import {
  Links,
  Meta,
  Outlet,
  Scripts,
  ScrollRestoration,
  type MetaFunction,
} from "react-router";
import "./index.css";
import { useThemeEffect } from "./hooks/useThemeEffect";
import { initHighlighter } from "./components/ui/CodeBlock";
import { getFullSitePath } from "./lib/utils";


export async function loader() {
  if (typeof window === "undefined") {
    await initHighlighter();
  }
}

export const meta: MetaFunction = () => {
  const fullBaseUrl = getFullSitePath();

  return [
    // Open Graph / Facebook
    { property: "og:image", content: `${fullBaseUrl}/razor_console_preview.png` },
    { property: "og:image:width", content: "1280" },
    { property: "og:image:height", content: "640" },

    // Twitter
    { name: "twitter:card", content: "summary_large_image" },
    { name: "twitter:image", content: `${fullBaseUrl}/razor_console_preview.png` },
    { property: "twitter:image:width", content: "1280" },
    { property: "twitter:image:height", content: "640" },
  ];
}

export default function Root() {
  useThemeEffect();
  const fullBaseUrl = getFullSitePath();

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
        <link rel="icon" type="image/svg+xml" href={`${fullBaseUrl}/razorconsole-icon.svg`} />
        <meta charSet="UTF-8" />
        <meta name="google-site-verification" content="jF1dcSGbDQJm6UY_MriNs2wHdnEGr_M1wZKiVciIdf8" />
        <meta name="viewport" content="width=device-width, initial-scale=1.0" />
        <link rel="preconnect" href="https://api.github.com" crossOrigin="anonymous" />
        <link rel="dns-prefetch" href="https://api.github.com" />
        <link
          rel="sitemap"
          href={`${fullBaseUrl}/sitemap.xml`}
          title="Sitemap"
        />
        <link
          rel="llms"
          href={`${fullBaseUrl}/llms.txt`}
          title="AI Documentation"
        />
        <link
          rel="llms-full"
          href={`${fullBaseUrl}/llms-full.txt`}
          title="Full AI Documentation"
        />
        <Meta />
        <Links />
      </head>
      <body id="root">
        <Outlet />
        <ScrollRestoration />
        <Scripts />
      </body>
    </html>
  );
}
