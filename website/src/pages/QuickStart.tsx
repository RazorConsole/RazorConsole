import { redirect } from "react-router"

export function loader() {
  return redirect("/docs/tutorial/hello-world")
}

export function clientLoader() {
  return redirect("/docs/tutorial/hello-world")
}

clientLoader.hydrate = true

export default function QuickStart() { return null }
