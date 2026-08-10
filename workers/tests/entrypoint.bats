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

# ---------------------------------------------------------------------------
# Security: ambient DOCKER_HOST must not survive the degraded path
# ---------------------------------------------------------------------------

# Write a fake claude that records the value of DOCKER_HOST (empty string if unset)
# at invocation time into a marker file so the test can assert it was cleared.
make_fake_claude_record_docker_host() {
    local docker_host_file="$BATS_TEST_TMPDIR/claude_docker_host"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
# Fake claude: record DOCKER_HOST (empty if unset) and exit 0
printf '%s' "\${DOCKER_HOST:-}" > "${docker_host_file}"
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

@test "degraded path (dockerd fail): ambient DOCKER_HOST is not passed to claude" {
    set_required_env
    make_fake_git
    make_fake_claude_record_docker_host
    make_fake_dockerd_fail
    make_fake_docker_unhealthy

    # Inject an ambient DOCKER_HOST that points at the host daemon
    export DOCKER_HOST="unix:///var/run/docker.sock"

    run bash "$ENTRYPOINT"

    # Entrypoint must succeed (degraded mode)
    [ "$status" -eq 0 ]

    # claude must have been invoked — marker file must exist
    local docker_host_file="$BATS_TEST_TMPDIR/claude_docker_host"
    [ -f "$docker_host_file" ]

    # The DOCKER_HOST seen by claude must be empty — not the injected host value
    local recorded_docker_host
    recorded_docker_host="$(cat "$docker_host_file")"
    [ -z "$recorded_docker_host" ]
}

# ---------------------------------------------------------------------------
# Auth-precondition tests (Step 4: credential-volume OAuth model)
# ---------------------------------------------------------------------------

# Helper: set all required env vars except any auth credential
set_required_env_no_auth() {
    unset ANTHROPIC_API_KEY
    unset CLAUDE_CODE_OAUTH_TOKEN
    export CLONE_URL="https://github.com/example/repo"
    export GIT_PAT="test-pat"
    export WORKER_PROMPT="test-prompt"
    export SYSTEM_PROMPT="test-system"
    export ISSUE_NUMBER="1"
    export CLAUDE_SETTINGS_JSON=""
}

@test "auth: ANTHROPIC_API_KEY alone is sufficient — entrypoint reaches claude" {
    make_fake_claude
    make_fake_git

    set_required_env_no_auth
    export ANTHROPIC_API_KEY="test-api-key"
    # Ensure no credential file exists in the default CLAUDE_CONFIG_DIR
    unset CLAUDE_CONFIG_DIR

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "auth: credential file at CLAUDE_CONFIG_DIR/.credentials.json is sufficient — entrypoint reaches claude" {
    make_fake_claude
    make_fake_git

    set_required_env_no_auth
    local cred_dir="$BATS_TEST_TMPDIR/claude-config"
    mkdir -p "$cred_dir"
    printf '{"token":"fake"}' > "$cred_dir/.credentials.json"
    export CLAUDE_CONFIG_DIR="$cred_dir"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "auth: neither ANTHROPIC_API_KEY nor credential file — entrypoint exits non-zero" {
    make_fake_git

    set_required_env_no_auth
    local cred_dir="$BATS_TEST_TMPDIR/claude-config-empty"
    mkdir -p "$cred_dir"
    # No .credentials.json created
    export CLAUDE_CONFIG_DIR="$cred_dir"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
}

@test "auth: neither ANTHROPIC_API_KEY nor credential file — error message is clear" {
    make_fake_git

    set_required_env_no_auth
    local cred_dir="$BATS_TEST_TMPDIR/claude-config-empty2"
    mkdir -p "$cred_dir"
    export CLAUDE_CONFIG_DIR="$cred_dir"

    run bash "$ENTRYPOINT"

    [[ "$output" == *"ANTHROPIC_API_KEY"* ]] || [[ "$output" == *"credentials"* ]]
}

@test "auth: CLAUDE_CODE_OAUTH_TOKEN alone (no credential file, no API key) — entrypoint exits non-zero" {
    make_fake_git

    set_required_env_no_auth
    export CLAUDE_CODE_OAUTH_TOKEN="old-oauth-token"
    local cred_dir="$BATS_TEST_TMPDIR/claude-config-token-only"
    mkdir -p "$cred_dir"
    export CLAUDE_CONFIG_DIR="$cred_dir"

    run bash "$ENTRYPOINT"

    # CLAUDE_CODE_OAUTH_TOKEN is no longer a valid auth mechanism
    [ "$status" -ne 0 ]
}

@test "auth: CLAUDE_CONFIG_DIR unset and no ANTHROPIC_API_KEY — entrypoint exits non-zero" {
    make_fake_git

    set_required_env_no_auth
    unset CLAUDE_CONFIG_DIR

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
}

# ---------------------------------------------------------------------------
# OAuth settings-write: atomic mv -f replaces a stale read-only settings.json
# ---------------------------------------------------------------------------

# ---------------------------------------------------------------------------
# Transient API error retry tests (is_transient_api_error + resume via claude -c)
# Mirrors ContainerOutputParser.IsTransientApiError — both must stay in sync.
# ---------------------------------------------------------------------------

# Write a fake claude that:
#   - On first call: emits a transient JSON line (5xx status) and exits non-zero
#   - On second call (-c resume): emits a success JSON line and exits 0
# Records a call-count marker under $BATS_TEST_TMPDIR.
make_fake_claude_transient_then_success() {
    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    local resume_args_file="$BATS_TEST_TMPDIR/claude_resume_args"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
# Fake claude: transient on first call, success on second (-c resume)
count=0
if [ -f "${call_count_file}" ]; then
    count=\$(cat "${call_count_file}")
fi
count=\$((count + 1))
printf '%d' "\$count" > "${call_count_file}"
if [ "\$count" -eq 1 ]; then
    printf '%s\n' '{"is_error":true,"api_error_status":503,"result":"API Error: Service Unavailable"}'
    exit 1
else
    # Record whether -c was passed on the resume call
    printf '%s\n' "\$*" > "${resume_args_file}"
    printf '%s\n' '{"is_error":false,"result":"Completed successfully","subtype":"success"}'
    exit 0
fi
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

# Write a fake claude that always emits a transient JSON line and exits non-zero.
# Used to assert that only ONE resume is attempted.
make_fake_claude_always_transient() {
    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
# Fake claude: always transient (5xx status)
count=0
if [ -f "${call_count_file}" ]; then
    count=\$(cat "${call_count_file}")
fi
count=\$((count + 1))
printf '%d' "\$count" > "${call_count_file}"
printf '%s\n' '{"is_error":true,"api_error_status":503,"result":"API Error: Service Unavailable"}'
exit 1
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

# Write a fake claude that emits a transient JSON line via known phrase
# (no numeric status) on first call; success on second.
make_fake_claude_phrase_transient_then_success() {
    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
# Fake claude: phrase-based transient on first call, success on second (-c resume)
count=0
if [ -f "${call_count_file}" ]; then
    count=\$(cat "${call_count_file}")
fi
count=\$((count + 1))
printf '%d' "\$count" > "${call_count_file}"
if [ "\$count" -eq 1 ]; then
    printf '%s\n' '{"is_error":true,"result":"API Error: 529 Overloaded"}'
    exit 1
else
    printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success"}'
    exit 0
fi
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

# Write a fake claude that emits a NON-transient failure on first call (no resume expected).
make_fake_claude_nontransient_failure() {
    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
# Fake claude: non-transient failure (is_error true, no 5xx, no known phrase)
count=0
if [ -f "${call_count_file}" ]; then
    count=\$(cat "${call_count_file}")
fi
count=\$((count + 1))
printf '%d' "\$count" > "${call_count_file}"
printf '%s\n' '{"is_error":true,"result":"some other error"}'
exit 1
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

# Write a fake claude that exits 0 on first call (success, no resume expected).
make_fake_claude_success_first() {
    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
# Fake claude: success on first call
count=0
if [ -f "${call_count_file}" ]; then
    count=\$(cat "${call_count_file}")
fi
count=\$((count + 1))
printf '%d' "\$count" > "${call_count_file}"
printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success"}'
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

@test "transient retry: claude is invoked twice when first call returns 5xx transient error" {
    set_required_env
    make_fake_git
    make_fake_claude_transient_then_success

    run bash "$ENTRYPOINT"

    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    [ -f "$call_count_file" ]
    local count
    count="$(cat "$call_count_file")"
    [ "$count" -eq 2 ]
}

@test "transient retry: resume call uses -c flag" {
    set_required_env
    make_fake_git
    make_fake_claude_transient_then_success

    run bash "$ENTRYPOINT"

    local resume_args_file="$BATS_TEST_TMPDIR/claude_resume_args"
    [ -f "$resume_args_file" ]
    local args
    args="$(cat "$resume_args_file")"
    [[ "$args" == *"-c"* ]]
}

@test "transient retry: last output line is the success JSON from the resume call" {
    set_required_env
    make_fake_git
    make_fake_claude_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    # The last line of stdout must be the resume's success JSON
    local last_line
    last_line="$(printf '%s\n' "$output" | grep '^{' | tail -1)"
    [[ "$last_line" == *'"is_error":false'* ]]
    [[ "$last_line" == *'"subtype":"success"'* ]]
}

@test "transient retry: when resume also fails transiently, exactly two claude calls are made" {
    set_required_env
    make_fake_git
    make_fake_claude_always_transient

    run bash "$ENTRYPOINT"

    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    [ -f "$call_count_file" ]
    local count
    count="$(cat "$call_count_file")"
    [ "$count" -eq 2 ]
}

@test "transient retry: when resume also fails transiently, last JSON line still carries transient signal" {
    set_required_env
    make_fake_git
    make_fake_claude_always_transient

    run bash "$ENTRYPOINT"

    local last_line
    last_line="$(printf '%s\n' "$output" | grep '^{' | tail -1)"
    # The last JSON must still classify as transient (5xx status)
    [[ "$last_line" == *'"api_error_status":503'* ]]
}

@test "transient retry: phrase-based transient (API Error: 529 Overloaded) triggers resume" {
    set_required_env
    make_fake_git
    make_fake_claude_phrase_transient_then_success

    run bash "$ENTRYPOINT"

    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    [ -f "$call_count_file" ]
    local count
    count="$(cat "$call_count_file")"
    [ "$count" -eq 2 ]
}

@test "transient retry: phrase-based transient resume succeeds — last JSON is success" {
    set_required_env
    make_fake_git
    make_fake_claude_phrase_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local last_line
    last_line="$(printf '%s\n' "$output" | grep '^{' | tail -1)"
    [[ "$last_line" == *'"is_error":false'* ]]
}

@test "transient retry: non-transient failure does NOT trigger resume (claude called once)" {
    set_required_env
    make_fake_git
    make_fake_claude_nontransient_failure

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    [ -f "$call_count_file" ]
    local count
    count="$(cat "$call_count_file")"
    [ "$count" -eq 1 ]
}

@test "transient retry: non-transient failure — exit code is non-zero (behaviour unchanged)" {
    set_required_env
    make_fake_git
    make_fake_claude_nontransient_failure

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
}

@test "transient retry: success on first call does NOT trigger resume (claude called once)" {
    set_required_env
    make_fake_git
    make_fake_claude_success_first

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    [ -f "$call_count_file" ]
    local count
    count="$(cat "$call_count_file")"
    [ "$count" -eq 1 ]
}

# ---------------------------------------------------------------------------

@test "oauth settings-write: CLAUDE_SETTINGS_JSON overwrites a pre-existing chmod 444 settings.json" {
    make_fake_claude
    make_fake_git

    # The entrypoint writes to ~/.claude/settings.json. Redirect HOME to a temp
    # dir so ~/.claude resolves to our controlled directory (matching the
    # production container layout where CLAUDE_CONFIG_DIR == ~/.claude).
    local fake_home="$BATS_TEST_TMPDIR/oauth-home"
    local cred_dir="$fake_home/.claude"
    mkdir -p "$cred_dir"

    # Pre-create a read-only settings.json — simulating the shared credential
    # volume after a previous worker run (the re-run breakage scenario).
    printf '{"old":"value"}' > "$cred_dir/settings.json"
    chmod 444 "$cred_dir/settings.json"

    # Provide a credential file so the auth check passes.
    printf '{"token":"fake"}' > "$cred_dir/.credentials.json"

    export HOME="$fake_home"
    export CLAUDE_CONFIG_DIR="$cred_dir"
    export CLAUDE_SETTINGS_JSON='{"new":"value"}'
    # Unset API key so only the credential-file auth path is active.
    unset ANTHROPIC_API_KEY

    export CLONE_URL="https://github.com/example/repo"
    export GIT_PAT="test-pat"
    export WORKER_PROMPT="test-prompt"
    export SYSTEM_PROMPT="test-system"
    export ISSUE_NUMBER="1"

    run bash "$ENTRYPOINT"

    # The entrypoint must succeed — the atomic mv -f must not be blocked by the 444 mode.
    [ "$status" -eq 0 ]

    # The settings.json must contain the new content written by this run.
    local written_content
    written_content="$(cat "$cred_dir/settings.json")"
    [[ "$written_content" == *'"new"'* ]]
}
