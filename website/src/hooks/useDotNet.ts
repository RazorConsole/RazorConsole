import { useEffect, useRef, useState } from "react"

const moduleCache = new Map<string, Promise<any>>()

export const useDotNet = (url: string) => {
  const dotnetUrl = useRef("")
  const [dotnet, setDotNet] = useState<any>(null)
  const [loading, setLoading] = useState(true)

  const load = async (currentUrl: string): Promise<any> => {
    const absoluteUrl = new URL(currentUrl, window.location.origin).toString()

    if (!moduleCache.has(absoluteUrl)) {
      const modulePromise = (async () => {
        const response = await fetch(absoluteUrl, { cache: "no-cache" })
        if (!response.ok) {
          throw new Error(
            `Failed to fetch ${absoluteUrl}: ${response.status} ${response.statusText}`
          )
        }

        const source = await response.text()
        const blobUrl = URL.createObjectURL(new Blob([source], { type: "text/javascript" }))
        try {
          return await import(/* @vite-ignore */ blobUrl)
        } finally {
          URL.revokeObjectURL(blobUrl)
        }
      })().catch((error) => {
        moduleCache.delete(absoluteUrl)
        throw error
      })

      moduleCache.set(absoluteUrl, modulePromise)
    }

    const module = await moduleCache.get(absoluteUrl)!

    console.log(`Loaded .NET module from ${absoluteUrl}`)

    const { getAssemblyExports, getConfig } = await module.dotnet
      .withDiagnosticTracing(false)
      .create()

    const config = getConfig()
    const exports = await getAssemblyExports(config.mainAssemblyName)
    return exports
  }

  useEffect(() => {
    if (dotnetUrl.current !== url) {
      // safeguard to prevent double-loading
      setLoading(true)
      dotnetUrl.current = url
      load(url)
        .then((exports) => setDotNet(exports))
        .finally(() => setLoading(false))
    }
  }, [url])
  return { dotnet, loading }
}
