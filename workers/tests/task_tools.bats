#!/usr/bin/env bats
# Image test: a worker's Claude Code session offers the task-list tools its system prompt asks
# it to use (TaskCreate / TaskUpdate), and Agent Activity reads. Current models leave them out
# unless the image opts in, so this checks the CLI's own tool list rather than the variable.
#
# Run against a built image: WORKER_IMAGE=foundry-worker:ci bats workers/tests/task_tools.bats
#
# Needs no credentials: `claude -p` writes its `system`/`init` event before it authenticates,
# then exits non-zero without credentials, so only the first stream line is read and the exit
# status is ignored.

setup() {
    if [ -z "${WORKER_IMAGE:-}" ]; then
        echo "WORKER_IMAGE must name the built worker image" >&2
        return 1
    fi
}

# Prints the tools listed by the init event of a `claude -p` session started with the given flags.
# Runs as node, the user workers run claude as (the CLI refuses --dangerously-skip-permissions as root).
init_tools() {
    local first_line
    first_line="$(docker run --rm --user node "$WORKER_IMAGE" \
        claude -p probe --output-format stream-json --verbose --model claude-opus-5-5 "$@" \
        2>/dev/null | head -n 1 || true)"
    jq -er 'select(.type == "system" and .subtype == "init") | .tools[]' <<<"$first_line"
}

assert_task_list_tools() {
    local tools="$1"
    for tool in TaskCreate TaskUpdate; do
        grep -qx "$tool" <<<"$tools" || { echo "missing $tool; init tools: $tools" >&2; return 1; }
    done
}

@test "the full capability's session lists TaskCreate and TaskUpdate" {
    run init_tools --dangerously-skip-permissions
    [ "$status" -eq 0 ]
    assert_task_list_tools "$output"
}

@test "the restricted capability's session lists TaskCreate and TaskUpdate" {
    run init_tools --restricted --strict-mcp-config
    [ "$status" -eq 0 ]
    assert_task_list_tools "$output"
}
