import { test } from "node:test"
import assert from "node:assert/strict"
import { setTimeout as delay } from "node:timers/promises"
import { attachTerminalInput } from "../src/lib/terminalInput.ts"

function source() {
  let data: (value: string) => void = () => {}
  let resize: (value: { cols: number; rows: number }) => void = () => {}
  return {
    onData(listener: typeof data) { data = listener; return { dispose() { data = () => {} } } },
    onResize(listener: typeof resize) { resize = listener; return { dispose() { resize = () => {} } } },
    send(value: string) { data(value) },
    size(cols: number, rows: number) { resize({ cols, rows }) },
  }
}

test("forwards keyboard, paste and SGR data exactly once, in order with resize", async () => {
  const terminal = source()
  const received: string[] = []
  const subscription = attachTerminalInput(terminal, async data => {
    await delay(5)
    received.push(data)
  }, async (cols, rows) => { received.push(`${cols}x${rows}`) })
  terminal.send("a")
  terminal.send("pasted text")
  terminal.send("\x1b[<65;3;2M")
  terminal.size(80, 24)
  await delay(130)
  subscription.dispose()
  assert.deepEqual(received, ["a", "pasted text", "\x1b[<65;3;2M", "80x24", ""])
})

test("disposal removes listeners and drops queued input and idle flush", async () => {
  const terminal = source()
  const received: string[] = []
  const subscription = attachTerminalInput(terminal, async data => { received.push(data) }, async () => {})
  terminal.send("a")
  subscription.dispose()
  terminal.send("b")
  await delay(100)
  assert.deepEqual(received, [])
})

test("failed dispatch is reported without breaking subsequent input", async () => {
  const terminal = source()
  const received: string[] = []
  const errors: unknown[] = []
  const subscription = attachTerminalInput(terminal, async data => {
    if (data === "bad") throw new Error("failed")
    received.push(data)
  }, async () => {}, error => { errors.push(error) })
  terminal.send("bad")
  terminal.send("ok")
  await delay(20)
  subscription.dispose()
  assert.equal(errors.length, 1)
  assert.deepEqual(received, ["ok"])
})
