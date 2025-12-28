import { Terminal } from "xterm"
import type { IDisposable, ITerminalOptions, ITheme } from "xterm"
import "xterm/css/xterm.css"

type TerminalConstructor = typeof Terminal
type TerminalType = InstanceType<typeof Terminal>

type TerminalOptions = Partial<ITerminalOptions> & { theme?: Partial<ITheme> }

type DotNetHelper = {
  invokeMethodAsync: (methodName: string, ...args: unknown[]) => Promise<unknown>
}

type RazorConsoleTerminalApi = {
  init: (elementId: string, options?: TerminalOptions) => void
  write: (elementId: string, text: string) => void
  clear: (elementId: string) => void
  dispose: (elementId: string) => void
  attachKeyListener: (elementId: string, helper: DotNetHelper) => void
}

declare global {
  interface Window {
    Terminal?: TerminalConstructor
    razorConsoleTerminal?: RazorConsoleTerminalApi
  }
}

const terminals = new Map<string, TerminalType>()
const keyHandlers = new Map<string, IDisposable>()

const defaultOptions: TerminalOptions = {
  convertEol: true,
  disableStdin: false,
  allowTransparency: false,
  theme: {
    background: "#1e1e1e",
    foreground: "#d4d4d4",
  },
  fontFamily: 'Consolas, "Courier New", monospace',
  fontSize: 14,
  lineHeight: 1.2,
  cursorBlink: false,
  scrollback: 1000,
}

function getTerminalConstructor(): TerminalConstructor {
  const ctor = typeof window !== "undefined" ? window.Terminal : undefined
  if (!ctor) {
    throw new Error("xterm.js is not loaded.")
  }

  return ctor
}

function mergeThemes(
  base: Partial<ITheme> | undefined,
  overrides: Partial<ITheme> | undefined
): Partial<ITheme> | undefined {
  if (!base && !overrides) {
    return undefined
  }

  return { ...(base ?? {}), ...(overrides ?? {}) }
}

function ensureHostElement(elementId: string): HTMLElement {
  const host = document.getElementById(elementId)
  if (!host) {
    throw new Error(`Element with id '${elementId}' was not found.`)
  }

  return host
}

function getExistingTerminal(elementId: string): TerminalType {
  const terminal = terminals.get(elementId)
  if (!terminal) {
    throw new Error(`Terminal with id '${elementId}' has not been initialized.`)
  }

  return terminal
}

export function isTerminalAvailable(): boolean {
  return typeof window !== "undefined" && typeof window.Terminal === "function"
}

export function registerTerminalInstance(elementId: string, terminal: TerminalType): void {
  terminals.set(elementId, terminal)
}

export function getTerminalInstance(elementId: string): TerminalType | undefined {
  return terminals.get(elementId)
}

export function initTerminal(elementId: string, options?: TerminalOptions): TerminalType {
  const TerminalCtor = getTerminalConstructor()
  const host = ensureHostElement(elementId)

  disposeTerminal(elementId)

  const mergedOptions: ITerminalOptions = {
    ...defaultOptions,
    ...options,
    theme: mergeThemes(defaultOptions.theme, options?.theme),
  }

  const terminal = new TerminalCtor(mergedOptions)
  host.innerHTML = ""
  terminal.open(host)
  terminals.set(elementId, terminal)
  return terminal
}

export function writeToTerminal(elementId: string, text: string): void {
  if (typeof text !== "string" || text.length === 0) {
    return
  }

  const terminal = getExistingTerminal(elementId)
  terminal.write(text)
}

export function clearTerminal(elementId: string): void {
  const terminal = getExistingTerminal(elementId)
  terminal.clear()
}

export function attachKeyListener(elementId: string, helper: DotNetHelper): void {
  const terminal = getExistingTerminal(elementId)

  keyHandlers.get(elementId)?.dispose()

  const subscription = terminal.onKey(async (event) => {
    const { key, domEvent } = event
    const { ctrlKey, metaKey, key: domKey } = domEvent

    // Handle Ctrl+C (or Cmd+C on Mac) - Copy selected text
    if ((ctrlKey || metaKey) && (domKey === "c" || domKey === "C")) {
      const selection = terminal.getSelection()
      if (selection) {
        try {
          await navigator.clipboard.writeText(selection)
          console.debug("Copied to clipboard:", selection)
        } catch (err) {
          console.warn("Failed to copy to clipboard:", err)
        }
      }
      // Still forward the event to WASM in case it needs to handle it
      void helper.invokeMethodAsync(
        "HandleKeyboardEvent",
        elementId,
        key,
        domEvent.key,
        domEvent.ctrlKey,
        domEvent.altKey,
        domEvent.shiftKey
      )
      return
    }

    // Handle Ctrl+V (or Cmd+V on Mac) - Paste from clipboard
    if ((ctrlKey || metaKey) && (domKey === "v" || domKey === "V")) {
      try {
        const text = await navigator.clipboard.readText()
        if (text) {
          console.debug("Pasting from clipboard:", text)
          // Send each character individually to WASM
          for (const char of text) {
            await helper.invokeMethodAsync(
              "HandleKeyboardEvent",
              elementId,
              char,
              char.length === 1 ? char : "Unidentified",
              false,
              false,
              false
            )
          }
        }
      } catch (err) {
        console.warn("Failed to paste from clipboard:", err)
      }
      return
    }

    // Forward all other keyboard events to WASM
    void helper.invokeMethodAsync(
      "HandleKeyboardEvent",
      elementId,
      key,
      domEvent.key,
      domEvent.ctrlKey,
      domEvent.altKey,
      domEvent.shiftKey
    )
  })

  keyHandlers.set(elementId, subscription)
}

export function disposeTerminal(elementId: string): void {
  keyHandlers.get(elementId)?.dispose()
  keyHandlers.delete(elementId)

  const terminal = terminals.get(elementId)
  if (!terminal) {
    return
  }

  terminal.dispose()
  terminals.delete(elementId)
}

function ensureGlobalApi(): void {
  if (typeof window === "undefined") {
    return
  }

  if (typeof window.Terminal !== "function") {
    window.Terminal = Terminal
  }

  const api: RazorConsoleTerminalApi = {
    init: (elementId, options) => {
      initTerminal(elementId, options)
    },
    write: writeToTerminal,
    clear: clearTerminal,
    dispose: disposeTerminal,
    attachKeyListener: (elementId, helper) => {
      attachKeyListener(elementId, helper)
    },
  }

  window.razorConsoleTerminal = api
}

ensureGlobalApi()

// ================================
// C# WASM Interop Functions
// ================================
// These functions call into the C# WebAssembly runtime via the razor-console module.
// We use DYNAMIC IMPORT here to prevent loading the heavy WASM module on initial page load.

import type { WasmExports } from "razor-console"

let wasmExportsPromise: Promise<WasmExports> | null = null

/**
 * Gets the WASM exports from main.js via the razor-console package.
 * Uses dynamic import to load the module only when needed.
 */
async function getWasmExports(): Promise<WasmExports> {
  if (wasmExportsPromise === null) {
    // Dynamic import: Webpack/Vite will split this into a separate chunk
    const { createRuntimeAndGetExports } = await import("razor-console")
    wasmExportsPromise = createRuntimeAndGetExports()
  }
  return wasmExportsPromise
}

/**
 * Registers a Razor component so its renderer can stream updates into the terminal.
 * Calls into C# WASM: Registry.RegisterComponent(elementId, cols, rows)
 * @param elementId - The ID of the terminal element to register
 * @param cols - The initial number of columns
 * @param rows - The initial number of rows
 */
export async function registerComponent(
  elementId: string,
  cols: number,
  rows: number
): Promise<void> {
  const exports = await getWasmExports()
  return exports.Registry.RegisterComponent(elementId, cols, rows)
}

/**
 * Forwards a keyboard event from xterm.js to the RazorConsole renderer.
 * Calls into C# WASM: Registry.HandleKeyboardEvent(componentName, xtermKey, domKey, ctrlKey, altKey, shiftKey)
 * @param componentName - The name of the component receiving the event
 * @param xtermKey - The key as reported by xterm.js
 * @param domKey - The key as reported by the DOM event
 * @param ctrlKey - Whether Ctrl was held
 * @param altKey - Whether Alt was held
 * @param shiftKey - Whether Shift was held
 */
export async function handleKeyboardEvent(
  componentName: string,
  xtermKey: string,
  domKey: string,
  ctrlKey: boolean,
  altKey: boolean,
  shiftKey: boolean
): Promise<void> {
  const exports = await getWasmExports()
  return exports.Registry.HandleKeyboardEvent(
    componentName,
    xtermKey,
    domKey,
    ctrlKey,
    altKey,
    shiftKey
  )
}

/**
 * Forwards a resize event from xterm.js to the RazorConsole renderer.
 * Calls into C# WASM: Registry.HandleResize(componentName, cols, rows)
 * @param componentName - The name of the component receiving the resize event
 * @param cols - The new number of columns
 * @param rows - The new number of rows
 */
export async function handleResize(
  componentName: string,
  cols: number,
  rows: number
): Promise<void> {
  const exports = await getWasmExports()
  return exports.Registry.HandleResize(componentName, cols, rows)
}

export type { DotNetHelper, RazorConsoleTerminalApi, TerminalOptions }
