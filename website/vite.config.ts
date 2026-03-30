import { defineConfig, type Plugin } from 'vite'
import { reactRouter } from "@react-router/dev/vite";
import path from 'path'
import tailwindcss from '@tailwindcss/vite'

function rewriteDotnetResourceImports(): Plugin {
  // Vite’s import-analysis tries to instantiate the .NET runtime .wasm files and fails because
  // they import the synthetic `env` module, which returns a 500. Rewriting to use `?url`
  // bypasses the wasm loader so Blazor fetches the raw bytes itself.
  const resourcePattern = /from\s+(['"])([^'"\n]+\.(?:wasm|dat))(\1)/g

  return {
    name: 'rewrite-dotnet-resource-imports',
    enforce: 'pre',
    transform(code, id) {
      if (!id.includes('_framework') || !id.endsWith('.js')) {
        return null
      }

      let didChange = false
      const rewritten = code.replace(resourcePattern, (match, quote, resourcePath) => {
        if (resourcePath.includes('?')) {
          return match
        }

        didChange = true
        const rewrittenSpecifier = `${resourcePath}?url`
        // Example: turns `import foo from "./System.Private.CoreLib.wasm"`
        // into `import foo from "./System.Private.CoreLib.wasm?url"`
        return `from ${quote}${rewrittenSpecifier}${quote}`
      })

      if (!didChange) {
        return null
      }

      return {
        code: rewritten,
        map: null,
      }
    },
  }
}
// https://vite.dev/config/
export default defineConfig({
  base: process.env.VITE_BASE ?? '/',
  plugins: [rewriteDotnetResourceImports(), reactRouter(), tailwindcss()],
  ssr: {
    noExternal: ['xterm', '@xterm/addon-fit']
  },
  server: {
    fs: {
      // Allow Vite to read files one level higher (for release-notes)
      allow: [".."],
    },
  },
  assetsInclude: ['**/*.dat'],
  optimizeDeps: {
    exclude: ['razor-console'],
  },
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
})
