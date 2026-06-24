#!/usr/bin/env bats
# Tests for start_rootless_dockerd in entrypoint.sh.
# All fake binaries are written into a per-test temp dir ($FAKE_BIN_DIR) so the
# committed helpers/ directory is never modified and tests are safe under parallel
# execution.

ENTRYPOINT="$(cd "$(dirname "$BATS_TEST_FILENAME")/.." && pwd)/entrypoint.sh"

setup() {
    # Create a per-test fake-bin directory and prepend it to PATH so fakes
    # shadow real binaries without touching committed files.
    export FAKE_BIN_DIR="$BATS_TEST_TMPDIR/bin"
    /bin/mkdir -p "$FAKE_BIN_DIR"
    export PATH="$FAKE_BIN_DIR:$PATH"
    export DOCKER_RETRY_COUNT=3
    export DOCKER_RETRY_SLEEP=0

    # Derive the expected runtime home the same way production does — via the
    # OS passwd database — so tests remain correct even when the runner sets a
    # custom $HOME that differs from the passwd entry.
    EXPECTED_RUNTIME_HOME="$(getent passwd "$(id -u)" | cut -d: -f6)"

    # Load start_rootless_dockerd directly from the entrypoint — no path redirect
    # needed because XDG_RUNTIME_DIR is now a passwd-sourced path that the test
    # user already owns.
    load_function
}

# Loads start_rootless_dockerd from entrypoint.sh into the current shell.
load_function() {
    local func_body
    func_body="$(awk '/^start_rootless_dockerd\(\)/,/^\}/' "$ENTRYPOINT")"
    eval "$func_body"
}

teardown() {
    # Kill any background jobs left by the test
    jobs -p | xargs -r kill 2>/dev/null || true
    # Clean up the runtime dir created by the function under test.
    # Use the same passwd-derived home as production so the correct dir is
    # removed even when $HOME differs from the passwd entry on the runner.
    rm -rf "${EXPECTED_RUNTIME_HOME}/.runtime"
}

# ---------------------------------------------------------------------------
# Helper: write a fake dockerd-rootless.sh that exits immediately
# ---------------------------------------------------------------------------
make_fake_dockerd_success() {
    cat > "$FAKE_BIN_DIR/dockerd-rootless.sh" <<'EOF'
#!/bin/bash
# Fake: exits 0 immediately (daemon "starts" instantly)
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/dockerd-rootless.sh"
}

# Helper: write a fake docker CLI that always reports a healthy daemon
make_fake_docker_healthy() {
    cat > "$FAKE_BIN_DIR/docker" <<'EOF'
#!/bin/bash
# Fake: 'docker -H <sock> version' returns success
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/docker"
}

# Helper: write a fake docker CLI that always fails (simulates daemon not ready)
make_fake_docker_unhealthy() {
    cat > "$FAKE_BIN_DIR/docker" <<'EOF'
#!/bin/bash
# Fake: daemon not ready
exit 1
EOF
    chmod +x "$FAKE_BIN_DIR/docker"
}

# Helper: write a fake dockerd-rootless.sh that hangs (background process)
make_fake_dockerd_background() {
    cat > "$FAKE_BIN_DIR/dockerd-rootless.sh" <<'EOF'
#!/bin/bash
# Fake daemon: close FD 3 (BATS' output fd) so the test harness does not block
# waiting on this backgrounded child, then stay alive briefly so the readiness
# poll observes a live daemon before exiting.
exec 3>&- 2>/dev/null || true
sleep 5
EOF
    chmod +x "$FAKE_BIN_DIR/dockerd-rootless.sh"
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
    # Assert against the concrete expected path derived from the passwd database
    # (same derivation as production) so a regression that changes both the
    # production path and the env var in lock-step is still caught.
    expected_sock="unix://${EXPECTED_RUNTIME_HOME}/.runtime/docker.sock"
    [ "$DOCKER_HOST" = "$expected_sock" ]
}

@test "start_rootless_dockerd creates XDG_RUNTIME_DIR before starting daemon" {
    make_fake_dockerd_background
    make_fake_docker_healthy

    # The function pins XDG_RUNTIME_DIR to <passwd-home>/.runtime.
    # Remove the dir so the function must re-create it.
    local expected_dir="${EXPECTED_RUNTIME_HOME}/.runtime"
    rm -rf "$expected_dir"
    start_rootless_dockerd
    [ -d "$expected_dir" ]
}

@test "start_rootless_dockerd creates XDG_RUNTIME_DIR with mode 0700" {
    make_fake_dockerd_background
    make_fake_docker_healthy

    local expected_dir="${EXPECTED_RUNTIME_HOME}/.runtime"
    rm -rf "$expected_dir"
    start_rootless_dockerd
    # stat --format=%a is available on Linux (GNU coreutils)
    local perms
    perms="$(stat --format='%a' "$expected_dir")"
    [ "$perms" = "700" ]
}

@test "start_rootless_dockerd creates a writable XDG_RUNTIME_DIR" {
    make_fake_dockerd_background
    make_fake_docker_healthy

    local expected_dir="${EXPECTED_RUNTIME_HOME}/.runtime"
    rm -rf "$expected_dir"
    start_rootless_dockerd
    [ -w "$expected_dir" ]
}

@test "start_rootless_dockerd is a no-op when dockerd-rootless.sh is absent" {
    # No fake written — command -v finds nothing in FAKE_BIN_DIR or PATH

    run start_rootless_dockerd
    [ "$status" -eq 0 ]
    # No daemon output should appear
    [[ "$output" != *"Starting rootless dockerd"* ]]
    [[ "$output" != *"Rootless dockerd is ready"* ]]
}

@test "start_rootless_dockerd does not export DOCKER_HOST when dockerd-rootless.sh is absent" {
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
# All external commands are stubbed via fakes so no real network/daemon needed.
# ---------------------------------------------------------------------------

# Write a fake claude that records it was called, then exits 0.
# The signal-file path is expanded at write-time (unquoted heredoc) so the
# generated script uses the concrete per-test path, not a shell variable.
make_fake_claude() {
    local signal_file="$BATS_TEST_TMPDIR/claude_called"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
# Fake claude: record invocation and exit 0
echo "claude-invoked" > "${signal_file}"
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

# Write a fake git that handles clone/remote/switch without a real repo
make_fake_git() {
    cat > "$FAKE_BIN_DIR/git" <<'EOF'
#!/bin/bash
# Fake git: handle the subcommands entrypoint uses, ignore the rest
case "${1:-}" in
    clone)  mkdir -p /workspace ;;
    -C)
        # git -C <dir> <subcommand> [args...] — ignore, just exit 0
        ;;
    *)      true ;;
esac
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/git"
}

@test "entrypoint invokes claude when dockerd-rootless.sh is absent (no-Docker path)" {
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

    run bash "$ENTRYPOINT"

    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
}

# ---------------------------------------------------------------------------
# Shared helpers for bootstrap-failure sentinel tests
# ---------------------------------------------------------------------------

# Minimal env vars needed for the entrypoint to reach the bootstrap steps
set_required_env() {
    export ANTHROPIC_API_KEY="test-key"
    export CLONE_URL="https://github.com/example/repo"
    export GIT_PAT="test-pat"
    export WORKER_PROMPT="test-prompt"
    export SYSTEM_PROMPT="test-system"
    export ISSUE_NUMBER="1"
    export CLAUDE_SETTINGS_JSON=""
}

# Write a fake git where clone exits non-zero (bootstrap-failure: clone)
make_fake_git_clone_fail() {
    cat > "$FAKE_BIN_DIR/git" <<'EOF'
#!/bin/bash
case "${1:-}" in
    clone) exit 1 ;;
    *)     true ;;
esac
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/git"
}

# Write a fake gh that exits non-zero (bootstrap-failure: auth)
make_fake_gh_fail() {
    cat > "$FAKE_BIN_DIR/gh" <<'EOF'
#!/bin/bash
exit 1
EOF
    chmod +x "$FAKE_BIN_DIR/gh"
}

# Write a fake git where switch exits non-zero (bootstrap-failure: branch)
make_fake_git_switch_fail() {
    cat > "$FAKE_BIN_DIR/git" <<'EOF'
#!/bin/bash
case "${1:-}" in
    clone)  mkdir -p /workspace ;;
    -C)     true ;;
    switch) exit 1 ;;
    *)      true ;;
esac
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/git"
}

# Write a fake dockerd-rootless.sh that exits non-zero immediately
make_fake_dockerd_fail() {
    cat > "$FAKE_BIN_DIR/dockerd-rootless.sh" <<'EOF'
#!/bin/bash
exit 1
EOF
    chmod +x "$FAKE_BIN_DIR/dockerd-rootless.sh"
}

# ---------------------------------------------------------------------------
# Bootstrap-failure sentinel tests
# ---------------------------------------------------------------------------

@test "clone failure emits FOUNDRY_BOOTSTRAP_FAILED stage=clone" {
    set_required_env
    make_fake_git_clone_fail

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=clone"* ]]
}

@test "clone failure does not emit stage=auth or stage=branch" {
    set_required_env
    make_fake_git_clone_fail

    run bash "$ENTRYPOINT"

    [[ "$output" != *"stage=auth"* ]]
    [[ "$output" != *"stage=branch"* ]]
}

@test "auth failure emits FOUNDRY_BOOTSTRAP_FAILED stage=auth" {
    set_required_env
    make_fake_git

    # Provide GH_TOKEN so the gh auth path is taken; fake gh fails
    export GH_TOKEN="test-gh-token"
    make_fake_gh_fail

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=auth"* ]]
}

@test "branch-switch failure emits FOUNDRY_BOOTSTRAP_FAILED stage=branch" {
    set_required_env
    make_fake_git_switch_fail

    # Set a branch name so the switch path is taken
    export BRANCH_NAME="feat/test-branch"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=branch"* ]]
}

@test "start_rootless_dockerd failure does NOT emit FOUNDRY_BOOTSTRAP_FAILED (trap disarmed)" {
    set_required_env
    make_fake_git
    make_fake_claude
    make_fake_docker_unhealthy
    make_fake_dockerd_fail

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$output" != *"FOUNDRY_BOOTSTRAP_FAILED"* ]]
}

@test "dockerd failure: entrypoint exits 0 and claude is still invoked (degraded mode)" {
    set_required_env
    make_fake_git
    make_fake_claude
    make_fake_dockerd_fail
    make_fake_docker_unhealthy

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "dockerd failure: entrypoint emits FOUNDRY_DOCKER_UNAVAILABLE sentinel" {
    set_required_env
    make_fake_git
    make_fake_claude
    make_fake_dockerd_fail
    make_fake_docker_unhealthy

    run bash "$ENTRYPOINT"

    [[ "$output" == *"FOUNDRY_DOCKER_UNAVAILABLE"* ]]
}

@test "dockerd failure: entrypoint does NOT emit FOUNDRY_BOOTSTRAP_FAILED" {
    set_required_env
    make_fake_git
    make_fake_claude
    make_fake_dockerd_fail
    make_fake_docker_unhealthy

    run bash "$ENTRYPOINT"

    [[ "$output" != *"FOUNDRY_BOOTSTRAP_FAILED"* ]]
}

@test "successful bootstrap emits no sentinel and invokes claude" {
    set_required_env
    make_fake_git
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$output" != *"FOUNDRY_BOOTSTRAP_FAILED"* ]]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "gh hostname contains @ guard emits FOUNDRY_BOOTSTRAP_FAILED stage=auth" {
    set_required_env
    make_fake_git

    # Craft a CLONE_URL whose hostname-derivation produces a string containing @
    export CLONE_URL="https://oauth2@github.example.com/org/repo"
    export GH_TOKEN="test-gh-token"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=auth"* ]]
}

@test "glab hostname contains @ guard emits FOUNDRY_BOOTSTRAP_FAILED stage=auth" {
    set_required_env
    make_fake_git

    # Craft a CLONE_URL whose hostname-derivation produces a string containing @
    export CLONE_URL="https://oauth2@gitlab.example.com/org/repo"
    export GITLAB_TOKEN="test-gitlab-token"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=auth"* ]]
}

@test "invalid BRANCH_NAME characters guard emits FOUNDRY_BOOTSTRAP_FAILED stage=branch" {
    set_required_env
    make_fake_git

    # A branch name with characters outside [a-zA-Z0-9_/.-]
    export BRANCH_NAME="feat/bad name with spaces"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=branch"* ]]
}
