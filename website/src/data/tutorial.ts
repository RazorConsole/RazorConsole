import helloWorld from "../../../tutorial/chapters/01-hello-world.md?raw"
import stateAndEvents from "../../../tutorial/chapters/02-state-and-events.md?raw"
import textInputAndFocus from "../../../tutorial/chapters/03-text-input-and-focus.md?raw"
import mouseEvents from "../../../tutorial/chapters/04-mouse-events.md?raw"
import widgetLayoutAndResize from "../../../tutorial/chapters/05-widget-layout-and-resize.md?raw"
import routing from "../../../tutorial/chapters/06-routing.md?raw"
import asyncWork from "../../../tutorial/chapters/07-async-work.md?raw"
import completeApp from "../../../tutorial/chapters/08-complete-app.md?raw"

export interface TutorialChapter {
  number: number
  slug: string
  shortTitle: string
  title: string
  description: string
  content: string
  componentId: string
  previewInstructions: string
  previewHeight?: string
}

export const tutorialChapters: TutorialChapter[] = [
  {
    number: 1,
    slug: "hello-world",
    shortTitle: "Hello World",
    title: "Chapter 1: Hello World",
    description: "Build and run your first RazorConsole component.",
    content: helloWorld,
    componentId: "TutorialHelloWorld",
    previewInstructions: "Click the terminal, then press Enter to activate Say hello.",
  },
  {
    number: 2,
    slug: "state-and-events",
    shortTitle: "State and Events",
    title: "Chapter 2: State and Events",
    description: "Update component state from terminal control callbacks.",
    content: stateAndEvents,
    componentId: "TutorialStateAndEvents",
    previewInstructions: "Use Tab to choose an action and Enter to update the counter.",
  },
  {
    number: 3,
    slug: "text-input-and-focus",
    shortTitle: "Text Input and Focus",
    title: "Chapter 3: Text Input and Focus",
    description: "Bind terminal input and manage keyboard focus.",
    content: textInputAndFocus,
    componentId: "TutorialTextInputAndFocus",
    previewInstructions: "Type in the focused field; use Tab and Shift+Tab to move focus.",
    previewHeight: "h-[360px]",
  },
  {
    number: 4,
    slug: "mouse-events",
    shortTitle: "Mouse Events",
    title: "Chapter 4: Mouse Events",
    description: "Subscribe to hover, click, wheel, and drag events.",
    content: mouseEvents,
    componentId: "TutorialMouseEvents",
    previewInstructions: "Hover and click the first card, scroll over the canvas, then drag the blue card.",
    previewHeight: "h-[460px]",
  },
  {
    number: 5,
    slug: "widget-layout-and-resize",
    shortTitle: "Widget Layout and Resize",
    title: "Chapter 5: Widget Layout and Resize",
    description: "Compose terminal-cell layouts that respond to resizing.",
    content: widgetLayoutAndResize,
    componentId: "TutorialWidgetLayoutAndResize",
    previewInstructions: "Resize the browser window and compare wrapping cards with expanding columns.",
    previewHeight: "h-[400px]",
  },
  {
    number: 6,
    slug: "routing",
    shortTitle: "Routing",
    title: "Chapter 6: Routing and Multi-page Apps",
    description: "Build routed terminal pages with NavigationManager.",
    content: routing,
    componentId: "TutorialRouting",
    previewInstructions: "Use the Home, Settings, and Missing buttons to exercise every router branch.",
    previewHeight: "h-[330px]",
  },
  {
    number: 7,
    slug: "async-work",
    shortTitle: "Async Work",
    title: "Chapter 7: Async Work, Loading, and Errors",
    description: "Render loading, success, failure, and cancellation states.",
    content: asyncWork,
    componentId: "TutorialAsyncWork",
    previewInstructions: "Run a successful load, simulate an error, or cancel while loading.",
    previewHeight: "h-[340px]",
  },
  {
    number: 8,
    slug: "complete-app",
    shortTitle: "Complete App",
    title: "Chapter 8: Complete Interactive App",
    description: "Combine the course concepts in a small task board.",
    content: completeApp,
    componentId: "TutorialCompleteApp",
    previewInstructions: "Add a task, click a task row to toggle it, and try the focused actions.",
    previewHeight: "h-[480px]",
  },
]

export function findTutorialChapter(slug?: string) {
  return tutorialChapters.find((chapter) => chapter.slug === slug)
}
