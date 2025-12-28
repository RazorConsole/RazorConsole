import { useState, useEffect } from "react"

interface GitHubRepo {
  stargazers_count: number
}

export function useGitHubStars(owner: string, repo: string) {
  const [stars, setStars] = useState<number | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<Error | null>(null)

  useEffect(() => {
    const abortController = new AbortController()

    const fetchStars = async () => {
      try {
        const response = await fetch(`https://api.github.com/repos/${owner}/${repo}`, {
          signal: abortController.signal,
        })
        if (!response.ok) {
          throw new Error("Failed to fetch repository data")
        }
        const data: GitHubRepo = await response.json()
        setStars(data.stargazers_count)
        setError(null)
      } catch (err) {
        // Ignore abort errors
        if (err instanceof Error && err.name === "AbortError") {
          return
        }
        setStars(null)
        setError(err instanceof Error ? err : new Error("Unknown error"))
      } finally {
        setLoading(false)
      }
    }

    fetchStars()

    return () => {
      abortController.abort()
    }
  }, [owner, repo])

  return { stars, loading, error }
}
