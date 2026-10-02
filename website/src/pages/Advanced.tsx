import { redirect } from "react-router"

export function loader() {
  return redirect("/blog/hot-reload/")
}

export const clientLoader = loader

export default function Advanced() { return null }
