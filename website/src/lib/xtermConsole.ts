import type { Terminal, IDisposable, ITerminalOptions, ITheme } from "xterm"
import "xterm/css/xterm.css"

type TerminalConstructor = typeof Terminal
type TerminalType = InstanceType<typeof Terminal>

export type TerminalOptions = Partial<ITerminalOptions> & { theme?: Partial<ITheme> }

export type DotNetHelper = {
  invokeMethodAsync: (methodName: string, ...args: unknown[]) => Promise<unknown>
}

export type RazorConsoleTerminalApi = {
  init: (elementId: string, options?: TerminalOptions) => Promise<void>
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

async function getTerminalConstructor(): Promise<TerminalConstructor> {
  if (typeof window === "undefined") {
    throw new Error("Terminal can only be initialized in the browser.")
  }
  
  if (window.Terminal) return window.Terminal

  const { Terminal } = await import("xterm")
  window.Terminal = Terminal
  return Terminal
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
  return typeof window !== "undefined" && (typeof window.Terminal === "function" || !!window.razorConsoleTerminal)
}

export function registerTerminalInstance(elementId: string, terminal: TerminalType): void {
  terminals.set(elementId, terminal)
}

export function getTerminalInstance(elementId: string): TerminalType | undefined {
  return terminals.get(elementId)
}

export async function initTerminal(elementId: string, options?: TerminalOptions): Promise<TerminalType> {
  const TerminalCtor = await getTerminalConstructor()
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
  if (typeof text !== "string" || text.length === 0) return
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
    }

    if ((ctrlKey || metaKey) && (domKey === "v" || domKey === "V")) {
      try {
        const text = await navigator.clipboard.readText()
        if (text) {
          console.debug("Pasting from clipboard:", text)
          // Send each character individually to WASM
          for (const char of text) {
            await helper.invokeMethodAsync("HandleKeyboardEvent", elementId, char, char, false, false, false)
          }
        }
      } catch (err) {
        console.warn("Failed to paste from clipboard:", err)
      }
      return
    }

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
  if (terminal) {
    terminal.dispose()
    terminals.delete(elementId)
  }
}

async function ensureGlobalApi(): Promise<void> {
  if (typeof window === "undefined") return

  const api: RazorConsoleTerminalApi = {
    init: async (elementId, options) => {
      await initTerminal(elementId, options)
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

if (typeof window !== "undefined") {
  ensureGlobalApi()
}

import type { WasmExports } from "razor-console"
let wasmExportsPromise: Promise<WasmExports> | null = null

async function getWasmExports(): Promise<WasmExports> {
  if (wasmExportsPromise === null) {
    const { createRuntimeAndGetExports } = await import("razor-console")
    wasmExportsPromise = createRuntimeAndGetExports()
  }
  return wasmExportsPromise
}

export async function registerComponent(elementId: string, cols: number, rows: number): Promise<void> {
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
  componentName: string, xtermKey: string, domKey: string, 
  ctrlKey: boolean, altKey: boolean, shiftKey: boolean
): Promise<void> {
  const exports = await getWasmExports()
  return exports.Registry.HandleKeyboardEvent(componentName, xtermKey, domKey, ctrlKey, altKey, shiftKey)
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