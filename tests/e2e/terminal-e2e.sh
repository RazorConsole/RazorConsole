#!/usr/bin/env bash

set -euo pipefail

readonly REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
readonly TMUX_SOCKET="razorconsole-e2e-$$"
readonly SESSION_NAME="razorconsole"
readonly TARGET="${SESSION_NAME}:0.0"
readonly WAIT_SECONDS=20

cleanup() {
  tmux -L "$TMUX_SOCKET" kill-server 2>/dev/null || true
}

capture_screen() {
  tmux -L "$TMUX_SOCKET" capture-pane -p -t "$TARGET"
}

pane_is_dead() {
  [[ "$(tmux -L "$TMUX_SOCKET" display-message -p -t "$TARGET" '#{pane_dead}')" == "1" ]]
}

fail() {
  local message="$1"
  printf 'ERROR: %s\n' "$message" >&2
  printf 'Captured terminal screen:\n' >&2
  capture_screen >&2 || true
  tmux -L "$TMUX_SOCKET" display-message -p -t "$TARGET" \
    'pane_dead=#{pane_dead} pane_dead_status=#{pane_dead_status}' >&2 || true
  exit 1
}

wait_for_pattern() {
  local pattern="$1"
  local description="$2"
  local deadline=$((SECONDS + WAIT_SECONDS))

  while ((SECONDS < deadline)); do
    if capture_screen 2>/dev/null | grep -Eq "$pattern"; then
      return 0
    fi

    if pane_is_dead; then
      fail "process exited while waiting for ${description}"
    fi

    sleep 0.1
  done

  fail "timed out waiting for ${description}"
}

start_example() {
  local project="$1"
  local rows="$2"
  local columns="$3"
  local command
  local quoted_root

  printf -v quoted_root '%q' "$REPO_ROOT"
  printf -v command '%q ' dotnet run \
    --project "$project" \
    --configuration Release \
    --framework net9.0 \
    --no-build \
    --no-restore

  tmux -L "$TMUX_SOCKET" new-session -d \
    -x "$columns" \
    -y "$rows" \
    -s "$SESSION_NAME" \
    "cd ${quoted_root} && exec ${command}"
  tmux -L "$TMUX_SOCKET" set-option -t "$SESSION_NAME" remain-on-exit on >/dev/null
}

stop_example() {
  tmux -L "$TMUX_SOCKET" send-keys -t "$TARGET" C-c

  local deadline=$((SECONDS + WAIT_SECONDS))
  while ((SECONDS < deadline)); do
    if pane_is_dead; then
      tmux -L "$TMUX_SOCKET" kill-session -t "$SESSION_NAME"
      return 0
    fi
    sleep 0.1
  done

  fail "process did not exit after Ctrl+C"
}

test_counter_interaction_and_resize() {
  start_example "examples/Counter/Counter.csproj" 24 80
  wait_for_pattern 'Current count:[[:space:]]+0' 'the initial counter screen'

  tmux -L "$TMUX_SOCKET" send-keys -t "$TARGET" Enter
  wait_for_pattern 'Current count:[[:space:]]+1' 'the incremented counter value'

  tmux -L "$TMUX_SOCKET" resize-window -t "$SESSION_NAME" -x 60 -y 18
  wait_for_pattern 'Current count:[[:space:]]+1' 'the counter after terminal resize'

  stop_example
}

test_select_input_burst() {
  start_example "examples/GallerySelect/GallerySelect.csproj" 24 80
  wait_for_pattern 'Select shows a focusable dropdown' 'the initial select screen'

  # 199 steps over six options ends on Border. Sending the keys as one burst
  # reproduces the render-dispatch pressure from fast terminal wheel input.
  tmux -L "$TMUX_SOCKET" send-keys -t "$TARGET" -N 199 Down
  tmux -L "$TMUX_SOCKET" send-keys -t "$TARGET" Enter
  wait_for_pattern '>[[:space:]]+Border' 'the select after a down-key burst'

  tmux -L "$TMUX_SOCKET" send-keys -t "$TARGET" -N 199 Up
  tmux -L "$TMUX_SOCKET" send-keys -t "$TARGET" Enter
  wait_for_pattern '>[[:space:]]+Align' 'the select after an up-key burst'

  if pane_is_dead; then
    fail "select process exited after burst input"
  fi

  stop_example
}

trap cleanup EXIT

command -v tmux >/dev/null || {
  printf 'ERROR: tmux is required to run terminal E2E tests.\n' >&2
  exit 1
}

test_counter_interaction_and_resize
test_select_input_burst

printf 'Terminal E2E tests passed.\n'
