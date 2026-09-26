import type { IDisposable } from "xterm"

type InputTerminal = {
  onData: (listener: (data: string) => void) => IDisposable
  onResize: (listener: (size: { cols: number; rows: number }) => void) => IDisposable
}

/** One ordered stream: no parallel onKey dispatch or manual paste injection. */
export function attachTerminalInput(
  terminal: InputTerminal,
  input: (data: string) => Promise<unknown>,
  resize: (cols: number, rows: number) => Promise<unknown>,
  reportError: (error: unknown) => void = console.error,
): IDisposable {
  let disposed = false
  let pending = Promise.resolve()
  let idle: ReturnType<typeof setTimeout> | undefined
  const enqueue = (action: () => Promise<unknown>) => {
    pending = pending.then(async () => {
      if (!disposed) await action()
    }).catch(reportError)
  }
  const dataSubscription = terminal.onData(data => {
    clearTimeout(idle)
    enqueue(() => input(data))
    // Start the idle interval after processing this chunk, not while it is queued.
    enqueue(async () => {
      clearTimeout(idle)
      idle = setTimeout(() => enqueue(() => input("")), 75)
    })
  })
  const resizeSubscription = terminal.onResize(({ cols, rows }) => {
    enqueue(() => resize(cols, rows))
  })
  return {
    dispose() {
      disposed = true
      clearTimeout(idle)
      dataSubscription.dispose()
      resizeSubscription.dispose()
    },
  }
}
