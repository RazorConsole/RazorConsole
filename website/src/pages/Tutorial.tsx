import { redirect, type LoaderFunctionArgs, type MetaFunction } from "react-router"
import { useParams } from "react-router-dom"
import { TutorialLayout } from "@/components/tutorial/TutorialLayout"
import { findTutorialChapter, tutorialChapters } from "@/data/tutorial"
import { getPageUrl } from "@/lib/utils"

export function loader({ params }: LoaderFunctionArgs) {
  if (!params.chapterId) return redirect("/docs/tutorial/hello-world/")
  if (!findTutorialChapter(params.chapterId)) throw new Response("Not Found", { status: 404 })
  return null
}

export const clientLoader = loader

export const meta: MetaFunction = ({ location }) => {
  const slug = location.pathname.split("/").filter(Boolean).at(-1)
  const chapter = findTutorialChapter(slug) ?? tutorialChapters[0]
  const title = `${chapter.title} | RazorConsole Tutorial`

  return [
    { title },
    { name: "description", content: chapter.description },
    { property: "og:title", content: title },
    { property: "og:description", content: chapter.description },
    { property: "og:url", content: getPageUrl(location.pathname) },
  ]
}

export default function Tutorial() {
  const { chapterId } = useParams()
  const chapter = findTutorialChapter(chapterId) ?? tutorialChapters[0]

  return <TutorialLayout chapter={chapter} />
}
