import { redirect, type LoaderFunctionArgs } from "react-router"

function destination(params: LoaderFunctionArgs["params"]) {
  return `/docs/tutorial/${params.chapterId || "hello-world"}/`
}

export function loader({ params }: LoaderFunctionArgs) {
  return redirect(destination(params))
}

export function clientLoader({ params }: LoaderFunctionArgs) {
  return redirect(destination(params))
}

clientLoader.hydrate = true

export default function TutorialRedirect() { return null }
