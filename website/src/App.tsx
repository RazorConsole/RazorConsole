import { BrowserRouter, Routes, Route } from "react-router-dom"
import { lazy, Suspense } from "react"
import Layout from "@/components/Layout"
import LoadingOverlay from "@/components/LoadingOverlay"
import { useThemeEffect } from "@/components/ThemeProvider"
import ScrollToTop from "@/components/ScrollToTop"

// --- Eager Imports ---
import Home from "@/pages/Home"
import Docs from "@/pages/Docs"
import QuickStart from "@/pages/QuickStart"
import Advanced from "@/pages/Advanced"
import Collaborators from "@/pages/Collaborators"
import Showcase from "@/pages/Showcase"
import ApiDocs from "@/pages/ApiDocs"

// --- Lazy Imports ---
const ComponentsLayout = lazy(() => import("@/pages/components/Layout"))
const ComponentsOverview = lazy(() => import("@/pages/components/Overview"))
const ComponentDetail = lazy(() => import("@/pages/components/Detail"))

function App() {
  useThemeEffect()

  return (
    <BrowserRouter basename={import.meta.env.BASE_URL}>
      <ScrollToTop/>
      <Routes>
        <Route path="/" element={<Layout />}>
          <Route index element={<Home />} />
          <Route path="docs/:topicId?" element={<Docs />} />
          <Route path="quick-start" element={<QuickStart />} />
          <Route path="api/:uid?" element={<ApiDocs />} />

          <Route path="components" element={
            <Suspense fallback={<LoadingOverlay text={"Importing WASM runtime"} />}>
              <ComponentsLayout />
            </Suspense>
          }>
            <Route index element={<ComponentsOverview />} />
            <Route path=":name" element={<ComponentDetail />} />
          </Route>

          <Route path="advanced" element={<Advanced />} />
          <Route path="collaborators" element={<Collaborators />} />
          <Route path="showcase" element={<Showcase />} />
        </Route>
      </Routes>
    </BrowserRouter>
  )
}

export default App
