# ChatClientAgent and coding tools

## Runtime

LLMAgentTUI uses `Microsoft.Agents.AI` 1.22.0 and
`Microsoft.Extensions.AI` / `.OpenAI` 10.10.0, the latest stable packages verified
against the NuGet v3 package indexes on 2026-09-25.

`ChatClientAgentController` is the UI adapter, not another agent implementation.
It owns one framework session created by `ChatClientAgent.CreateSessionAsync`.
`RunStreamingAsync` supplies text chunks, function calls/results, usage, and
`ToolApprovalRequestContent`. There is no IChatService or separate hand-managed
model conversation history. Approval responses use the native request's
`CreateResponse` and resume the same framework session. Multiple pending tool
calls are approved individually, then submitted together. Escape cancels a live
run or rejects outstanding approvals (their rejection is sent on the next turn).

The composer remains responsive during a run. Enter does not discard a queued
draft while busy. Tool output can be expanded and selected; approval arguments
have a scrollable text viewport. Model and tool text is escaped for terminal
control characters before display; the framework retains the original data.

`--mock` still selects a deterministic session fixture for rapid UI tests.
Separate integration tests use the real ChatClientAgent and real TUI, replacing
only the model transport; they exercise streaming, history, tools, native
approval/denial, token usage and cancellation without network credentials.

## pi source research

Reference: [badlogic/pi-mono](https://github.com/badlogic/pi-mono), commit
`d6af72e1857cfb10b41d8ff8e69f0d72b4cf6d31`. This is a design reference, not a
vendored implementation. Source paths below are relative to
`packages/coding-agent/src/core/tools/` at that revision.

| Source | Behavior worth retaining |
| --- | --- |
| `index.ts` | Default coding tools are read/bash/edit/write. Separate read-only set is read/grep/find/ls. PowerShell is also available. |
| `read.ts` | 1-based offset/limit; text and image content; actionable continuation after truncation. |
| `truncate.ts` | Independent 2,000-line and 50 KiB caps. Head for reads, tail for command output. |
| `bash.ts`, `output-accumulator.ts` | Stream output, bound in-memory capture, retain full output when truncated, terminate process trees on timeout/abort. Optional timeout has no default in pi. |
| `edit.ts`, `edit-diff.ts` | Multiple unique, non-overlapping replacements against the original file; preserve BOM and line endings; return diff metadata. |
| `file-mutation-queue.ts` | Serialize mutations of the same real path; different files can proceed concurrently. |
| `write.ts` | Create parent directories and write contents through replaceable filesystem operations. |
| `grep.ts`, `find.ts`, `ls.ts` | Dedicated bounded search/listing, using ripgrep/fd for search; predictable results and truncation notices. |
| `path-utils.ts` | Resolve against cwd; handle home paths and macOS filename variants. |

Read the pinned [tool source directory](https://github.com/badlogic/pi-mono/tree/d6af72e1857cfb10b41d8ff8e69f0d72b4cf6d31/packages/coding-agent/src/core/tools)
for exact schemas; old descriptions of pi's edit schema are not the current
multi-edit implementation.

## Implemented subset and deliberate differences

- `read`: UTF-8 text only, 1-based paging, 2,000-line/50 KiB output limits;
  files capped at 2 MiB. Images and larger-file streaming are not implemented.
- `write`: UTF-8 create/replace, parent directory creation, 2 MiB size cap.
- `edit`: one exact unique replacement, preserving untouched bytes as UTF-8
  text (including BOM and existing line endings). No fuzzy matching, multi-edit
  schema, newline normalization or structured unified diff yet.
- Both mutations share a gate and use same-directory temporary files plus
  atomic replacement so cancellation before commit does not truncate the target.
  Existing Unix permission bits are preserved. This does not preserve every
  filesystem-specific metadata field or serialize independent app processes.
- `bash` on Unix / `powershell` on Windows: explicit working directory,
  noninteractive execution, bounded combined stdout/stderr tail, exit status,
  default 120-second timeout (maximum 600) and process-tree cancellation.
  Full truncated output is not archived and incremental shell output is not yet
  surfaced to the UI. PowerShell requires `pwsh` installed.
- `grep`, `find`, `ls` remain research-backed follow-up work; an approved shell
  can run existing search utilities in the meantime.

Every registered function is wrapped in `ApprovalRequiredAIFunction`, including
reads because their results may be sent to a remote model. No blanket approval
is persisted. File tools reject lexical paths outside the workspace and linked
path components. These checks are guardrails, **not an OS sandbox**: they cannot
defend against all concurrent filesystem replacement or hard-link attacks.
An approved shell command can access the network and files outside the workspace
with the user's permissions. Run in a container for untrusted projects.

## Provider configuration

The `Agent` configuration section controls provider label, endpoint, model and
API key through standard .NET JSON, Development User Secrets, optional ignored
`appsettings.local.json`, environment and command-line providers. The shipped
default is DeepSeek with a `dummy` key. Local JSON overrides User Secrets but
not environment variables or command-line arguments; it is not published.
See the README for setup; old `OPENAI_API_KEY` and `LLMAGENT_*` variables are no
longer used. The obsolete preview Ollama adapter was removed rather than mixed
with modern abstractions; Ollama can use its OpenAI-compatible `/v1` endpoint.

The chosen server/model must support streaming function calling. Live provider
compatibility and native Windows process behavior are not proven by the offline
tests. Tools are owned by the example, not RazorConsole.Core.
