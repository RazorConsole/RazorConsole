import { defineConfig } from "vite"

// Standalone fixture server: does not depend on the documentation router/loaders.
export default defineConfig({
  server: { host: "127.0.0.1", port: 5174, fs: { allow: [".."] } },
  optimizeDeps: { exclude: ["razor-console"] },
  assetsInclude: ["**/*.dat"],
  plugins: [{
    name: "terminal-fixture-wasm-urls",
    enforce: "pre",
    transform(code, id) {
      if (!id.includes("_framework") || !id.endsWith(".js")) return
      return code.replace(/from\s+(['"])([^'"\n]+\.(?:wasm|dat))\1/g,
        (_, quote, path) => `from ${quote}${path}?url${quote}`)
    },
  }],
})
