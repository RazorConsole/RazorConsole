import { BrowserRouter, Routes, Route } from "react-router-dom"
import { Suspense } from "react"
import Layout from "@/components/app/Layout"
import LoadingOverlay from "@/components/app/LoadingOverlay"
import { useThemeEffect } from "@/hooks/useThemeEffect"
import ScrollToTop from "@/components/app/ScrollToTop"

// --- Eager Imports ---
import Home from "@/pages/Home"
import Docs from "@/pages/Docs"
import QuickStart from "@/pages/QuickStart"
import Advanced from "@/pages/Advanced"
import Collaborators from "@/pages/Collaborators"
import Showcase from "@/pages/Showcase"
import ApiDocs from "@/pages/ApiDocs"
import ComponentsLayout from "./pages/components/Layout"
import ComponentsOverview from "./pages/components/Overview"
import ComponentDetail from "./pages/components/Detail"

function App() {
  useThemeEffect()

  return (
    <BrowserRouter basename={import.meta.env.BASE_URL}>
      <ScrollToTop />
      <Routes>
        <Route path="/" element={<Layout />}>
          <Route index element={<Home />} />
          <Route path="docs/:topicId?" element={<Docs />} />
          <Route path="quick-start" element={<QuickStart />} />
          <Route path="api/:uid?" element={<ApiDocs />} />

          <Route
            path="components"
            element={
              <Suspense fallback={<LoadingOverlay text={"Importing WASM runtime"} />}>
                <ComponentsLayout />
              </Suspense>
            }
          >
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
