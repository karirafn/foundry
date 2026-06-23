#!/usr/bin/env bats
# Tests for start_rootless_dockerd in entrypoint.sh.
# Fake binaries in helpers/ stand in for dockerd-rootless.sh and docker so no
# real daemon is needed.

HELPERS_DIR="$(cd "$(dirname "$BATS_TEST_FILENAME")/helpers" && pwd)"
ENTRYPOINT="$(cd "$(dirname "$BATS_TEST_FILENAME")/.." && pwd)/entrypoint.sh"

# Source only the start_rootless_dockerd function (not the whole script which
# checks env vars and calls claude).  We achieve this by exporting the function
# after sourcing a minimal wrapper that defines it.
load_function() {
    # Extract and eval only the function body so bats can call it directly.
    # The function is delimited by "start_rootless_dockerd()" … closing "}" on
    # its own line.  We use awk to pull it out.
    eval "$(awk '/^start_rootless_dockerd\(\)/,/^\}/' "$ENTRYPOINT")"
}

setup() {
    # Put helpers first so fakes shadow real binaries
    export PATH="$HELPERS_DIR:$PATH"
    # Reset runtime dir to a temp location each test
    export XDG_RUNTIME_DIR="$(mktemp -d)"
    export DOCKER_RETRY_COUNT=3
    export DOCKER_RETRY_SLEEP=0
    load_function
}

teardown() {
    rm -rf "$XDG_RUNTIME_DIR"
    # Kill any background jobs left by the test
    jobs -p | xargs -r kill 2>/dev/null || true
}

# ---------------------------------------------------------------------------
# Helper: write a fake dockerd-rootless.sh that exits immediately
# ---------------------------------------------------------------------------
make_fake_dockerd_success() {
    cat > "$HELPERS_DIR/dockerd-rootless.sh" <<'EOF'
#!/bin/bash
# Fake: exits 0 immediately (daemon "starts" instantly)
exit 0
EOF
    chmod +x "$HELPERS_DIR/dockerd-rootless.sh"
}

# Helper: write a fake docker CLI that always reports a healthy daemon
make_fake_docker_healthy() {
    cat > "$HELPERS_DIR/docker" <<'EOF'
#!/bin/bash
# Fake: 'docker -H <sock> version' returns success
exit 0
EOF
    chmod +x "$HELPERS_DIR/docker"
}

# Helper: write a fake docker CLI that always fails (simulates daemon not ready)
make_fake_docker_unhealthy() {
    cat > "$HELPERS_DIR/docker" <<'EOF'
#!/bin/bash
# Fake: daemon not ready
exit 1
EOF
    chmod +x "$HELPERS_DIR/docker"
}

# Helper: write a fake dockerd-rootless.sh that hangs (background process)
make_fake_dockerd_background() {
    cat > "$HELPERS_DIR/dockerd-rootless.sh" <<'EOF'
#!/bin/bash
# Fake: runs in background, stays alive so the process can be tracked
sleep 9999 &
wait
EOF
    chmod +x "$HELPERS_DIR/dockerd-rootless.sh"
}

# ---------------------------------------------------------------------------
# Test 1 — happy path: daemon starts, socket becomes ready, DOCKER_HOST exported
# ---------------------------------------------------------------------------
@test "start_rootless_dockerd exports DOCKER_HOST when daemon becomes ready" {
    make_fake_dockerd_background
    make_fake_docker_healthy

    run start_rootless_dockerd
    [ "$status" -eq 0 ]
    # The function must print that it started successfully
    [[ "$output" == *"Rootless dockerd is ready"* ]]
}

@test "start_rootless_dockerd sets DOCKER_HOST to the socket under XDG_RUNTIME_DIR" {
    make_fake_dockerd_background
    make_fake_docker_healthy

    # Call in the same shell so we can inspect exported vars
    start_rootless_dockerd
    expected_sock="unix://${XDG_RUNTIME_DIR}/docker.sock"
    [ "$DOCKER_HOST" = "$expected_sock" ]
}

@test "start_rootless_dockerd creates XDG_RUNTIME_DIR before starting daemon" {
    make_fake_dockerd_background
    make_fake_docker_healthy

    # Remove the dir — the function must re-create it
    rm -rf "$XDG_RUNTIME_DIR"
    start_rootless_dockerd
    [ -d "$XDG_RUNTIME_DIR" ]
}

@test "start_rootless_dockerd is a no-op when dockerd-rootless.sh is absent" {
    # Remove the fake so command -v finds nothing
    rm -f "$HELPERS_DIR/dockerd-rootless.sh"

    run start_rootless_dockerd
    [ "$status" -eq 0 ]
    # No daemon output should appear
    [[ "$output" != *"Starting rootless dockerd"* ]]
    [[ "$output" != *"Rootless dockerd is ready"* ]]
}

@test "start_rootless_dockerd does not export DOCKER_HOST when dockerd-rootless.sh is absent" {
    rm -f "$HELPERS_DIR/dockerd-rootless.sh"
    unset DOCKER_HOST

    # Call directly (not via run) so exported vars are visible
    start_rootless_dockerd
    [ -z "${DOCKER_HOST:-}" ]
}

# ---------------------------------------------------------------------------
# Test 2 — timeout path: daemon never becomes ready → exit non-zero
# ---------------------------------------------------------------------------
@test "start_rootless_dockerd exits non-zero on readiness timeout" {
    make_fake_dockerd_background
    make_fake_docker_unhealthy

    run start_rootless_dockerd
    [ "$status" -ne 0 ]
}

@test "start_rootless_dockerd prints diagnostic message on timeout" {
    make_fake_dockerd_background
    make_fake_docker_unhealthy

    run start_rootless_dockerd
    [[ "$output" == *"timed out"* ]] || [[ "$output" == *"Timed out"* ]]
}

# ---------------------------------------------------------------------------
# AC4 — DOCKER_HOST uses a unix:// URI so Testcontainers .NET derives the
# correct Ryuk socket bind-mount path without TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE.
# (Testcontainers checks the URI scheme; "unix" → extracts AbsolutePath as source)
# ---------------------------------------------------------------------------
@test "start_rootless_dockerd exports DOCKER_HOST as a unix:// URI for Testcontainers Ryuk compat" {
    make_fake_dockerd_background
    make_fake_docker_healthy

    start_rootless_dockerd
    [[ "$DOCKER_HOST" == unix://* ]]
}

# ---------------------------------------------------------------------------
# AC5 — full entrypoint proof: when dockerd-rootless.sh is absent the
# entrypoint is a no-op for Docker setup and still invokes claude.
# All external commands are stubbed via helpers so no real network/daemon needed.
# ---------------------------------------------------------------------------

# Write a fake claude that records it was called, then exits 0
make_fake_claude() {
    cat > "$HELPERS_DIR/claude" <<'EOF'
#!/bin/bash
# Fake claude: record invocation and exit 0
echo "claude-invoked" > /tmp/bats_claude_called
exit 0
EOF
    chmod +x "$HELPERS_DIR/claude"
}

# Write a fake git that handles clone/remote/switch without a real repo
make_fake_git() {
    cat > "$HELPERS_DIR/git" <<'EOF'
#!/bin/bash
# Fake git: handle the subcommands entrypoint uses, ignore the rest
case "${1:-}" in
    clone)  mkdir -p /workspace ;;
    -C)     shift; shift; shift; shift ;;  # git -C /workspace remote set-url origin ...
    *)      true ;;
esac
exit 0
EOF
    chmod +x "$HELPERS_DIR/git"
}

@test "entrypoint invokes claude when dockerd-rootless.sh is absent (no-Docker path)" {
    rm -f "$HELPERS_DIR/dockerd-rootless.sh"
    make_fake_claude
    make_fake_git

    # Minimal required env vars for the entrypoint
    export ANTHROPIC_API_KEY="test-key"
    export CLONE_URL="https://github.com/example/repo"
    export GIT_PAT="test-pat"
    export WORKER_PROMPT="test-prompt"
    export SYSTEM_PROMPT="test-system"
    export ISSUE_NUMBER="1"
    export CLAUDE_SETTINGS_JSON=""

    rm -f /tmp/bats_claude_called

    run bash "$ENTRYPOINT"

    [ -f /tmp/bats_claude_called ]
    rm -f /tmp/bats_claude_called
}
