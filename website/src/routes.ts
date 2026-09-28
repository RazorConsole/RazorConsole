import { type RouteConfig, index, layout, route } from "@react-router/dev/routes"

export default [
  layout("./components/app/Layout.tsx", [
    index("./pages/Home.tsx"),
    route("docs/:topicId?", "./pages/Docs.tsx"),
    route("docs/tutorial/:chapterId?", "./pages/Tutorial.tsx"),
    route("blog/:topicId?", "./pages/Blog.tsx"),
    route("quick-start", "./pages/QuickStart.tsx"),
    route("tutorial/:chapterId?", "./pages/TutorialRedirect.tsx"),
    route("release-notes/:version?", "./pages/ReleaseNotes.tsx"),
    route("api/:uid?", "./pages/ApiDocs.tsx"),

    route("components", "./pages/components/Layout.tsx", [
      index("./pages/components/Overview.tsx"),
      route(":name", "./pages/components/Detail.tsx"),
    ]),

    route("advanced", "./pages/Advanced.tsx"),
    route("collaborators", "./pages/Collaborators.tsx"),
    route("gallery", "./pages/Gallery.tsx"),
    route("showcase", "./pages/Showcase.tsx"),
  ]),
] satisfies RouteConfig
