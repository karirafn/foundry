#!/usr/bin/env bats
# Tests for entrypoint.sh.
# All fake binaries are written into a per-test temp dir ($FAKE_BIN_DIR) so the
# committed helpers/ directory is never modified and tests are safe under parallel
# execution.

ENTRYPOINT="$(cd "$(dirname "$BATS_TEST_FILENAME")/.." && pwd)/entrypoint.sh"

# Testcontainers connection environment owned by start_rootless_dockerd.
TESTCONTAINERS_CONNECTION_VARS=(
    TESTCONTAINERS_HOST_OVERRIDE
    TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE
    TESTCONTAINERS_RYUK_DISABLED
    TESTCONTAINERS_RYUK_CONTAINER_IMAGE
    TESTCONTAINERS_HUB_IMAGE_NAME_PREFIX
    TESTCONTAINERS_RYUK_PRIVILEGED
    TESTCONTAINERS_RYUK_CONTAINER_PRIVILEGED
    TESTCONTAINERS_REUSE_ENABLE
    TESTCONTAINERS_FUTURE_SETTING
)

setup() {
    # Create a per-test fake-bin directory and prepend it to PATH so fakes
    # shadow real binaries without touching committed files.
    export FAKE_BIN_DIR="$BATS_TEST_TMPDIR/bin"
    /bin/mkdir -p "$FAKE_BIN_DIR"
    export PATH="$FAKE_BIN_DIR:$PATH"
    export DOCKER_RETRY_COUNT=3
    export DOCKER_RETRY_SLEEP=0

    # The entrypoint deletes ~/.claude/settings.json (and $CLAUDE_CONFIG_DIR/settings.json)
    # on every start, so isolate HOME and drop any ambient CLAUDE_CONFIG_DIR — a local bats
    # run must never touch the developer's own Claude Code settings.
    export HOME="$BATS_TEST_TMPDIR/home"
    /bin/mkdir -p "$HOME/.claude"
    unset CLAUDE_CONFIG_DIR CLAUDE_SECURESTORAGE_CONFIG_DIR

    # Full-entrypoint tests run as the non-root CI user, so they exercise the non-root
    # phase, which first verifies /etc/claude-code/managed-settings.json via stat. Fake a
    # valid root-owned 0644 file (see make_fake_stat) rather than provisioning a live
    # managed policy in the runner's own /etc.
    make_fake_stat

    # Full-entrypoint tests resolve the run's role plugins before cloning. Point the
    # entrypoint at a per-test roles tree holding the implement and shared plugins
    # (the image bakes the real one at /opt/foundry/roles).
    make_role_fixture "$BATS_TEST_TMPDIR/roles" implement shared
    export FOUNDRY_ROLES_DIR="$BATS_TEST_TMPDIR/roles"
    export WORKER_ROLE="implement"

    # Derive the expected runtime home the same way production does — via the
    # OS passwd database — so tests remain correct even when the runner sets a
    # custom $HOME that differs from the passwd entry.
    EXPECTED_RUNTIME_HOME="$(getent passwd "$(id -u)" | cut -d: -f6)"

    # Load start_rootless_dockerd directly from the entrypoint — no path redirect
    # needed because XDG_RUNTIME_DIR is now a passwd-sourced path that the test
    # user already owns.
    load_function
}

# Fake `stat` that answers for the managed settings file only and defers to the real
# binary for every other path.  FAKE_MANAGED_STAT is the "<uid> <octal mode>" it prints
# (default: a valid root-owned 0644 file); FAKE_MANAGED_STAT=missing makes it fail as
# for a nonexistent file.
make_fake_stat() {
    local real_stat
    real_stat="$(command -v stat)"
    cat > "$FAKE_BIN_DIR/stat" <<EOF
#!/bin/bash
if [[ "\${!#}" == "/etc/claude-code/managed-settings.json" ]]; then
    case "\${FAKE_MANAGED_STAT:-0 644}" in
        missing) echo "stat: cannot statx '/etc/claude-code/managed-settings.json': No such file or directory" >&2; exit 1 ;;
        *) printf '%s\\n' "\${FAKE_MANAGED_STAT:-0 644}"; exit 0 ;;
    esac
fi
exec "${real_stat}" "\$@"
EOF
    chmod +x "$FAKE_BIN_DIR/stat"
}

# make_role_fixture <roles_dir> <role>... — writes a minimal plugin manifest for each
# named role under <roles_dir>.
make_role_fixture() {
    local roles_dir="$1"
    shift
    local role
    for role in "$@"; do
        /bin/mkdir -p "$roles_dir/$role/.claude-plugin"
        printf '{"name":"foundry-role-%s","description":"fixture"}\n' "$role" \
            > "$roles_dir/$role/.claude-plugin/plugin.json"
    done
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

@test "start_rootless_dockerd exports TESTCONTAINERS_HOST_OVERRIDE=127.0.0.1 when daemon becomes ready" {
    make_fake_dockerd_background
    make_fake_docker_healthy

    # Call in the same shell so we can inspect exported vars
    start_rootless_dockerd
    # Published ports of nested containers are reachable only on the worker's
    # loopback (pinned to IPv4 because the name localhost may resolve to ::1 first), not
    # on the bridge gateway Testcontainers would otherwise pick.
    [ "${TESTCONTAINERS_HOST_OVERRIDE:-}" = "127.0.0.1" ]
}

@test "start_rootless_dockerd leaves only TESTCONTAINERS_HOST_OVERRIDE of the inherited TESTCONTAINERS_ variables when daemon becomes ready" {
    make_fake_dockerd_background
    make_fake_docker_healthy
    local name
    for name in "${TESTCONTAINERS_CONNECTION_VARS[@]}"; do
        export "$name=inherited.example"
    done

    # Call in the same shell so we can inspect exported vars
    start_rootless_dockerd

    local survivors
    survivors="$(compgen -e | grep '^TESTCONTAINERS_' | sort | tr '\n' ' ' || true)"
    [ "$survivors" = "TESTCONTAINERS_HOST_OVERRIDE " ] || { echo "survivors: $survivors" >&2; return 1; }
    [ "$TESTCONTAINERS_HOST_OVERRIDE" = "127.0.0.1" ]
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

@test "start_rootless_dockerd does not export TESTCONTAINERS_HOST_OVERRIDE when dockerd-rootless.sh is absent" {
    export TESTCONTAINERS_HOST_OVERRIDE="inherited.example"

    # Call directly (not via run) so exported vars are visible
    start_rootless_dockerd
    [ -z "${TESTCONTAINERS_HOST_OVERRIDE:-}" ]
}

@test "start_rootless_dockerd unsets inherited Testcontainers connection variables when dockerd-rootless.sh is absent" {
    local name
    for name in "${TESTCONTAINERS_CONNECTION_VARS[@]}"; do
        export "$name=inherited.example"
    done

    # Call directly (not via run) so exported vars are visible
    start_rootless_dockerd

    for name in "${TESTCONTAINERS_CONNECTION_VARS[@]}"; do
        [ -z "${!name:-}" ] || { echo "$name survived: ${!name}" >&2; return 1; }
    done
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

# ---------------------------------------------------------------------------
# Helper: build a sanitized PATH that excludes the system gh and glab binaries
# while retaining all other tools the entrypoint needs.
# Required because gh and glab may already be installed on the test host (they
# are present in the worker image by design); the absent-CLI tests must prevent
# command -v from finding them. Placing FAKE_BIN_DIR first is insufficient when
# no fake is written — command -v walks PATH until it finds an executable match.
# This helper symlinks all /usr/bin and /bin executables EXCEPT gh and glab into
# a per-test sanitized dir and prepends it, so the real binaries are shadowed.
make_sanitized_path_without() {
    # Args: names to exclude (e.g. "gh" or "glab")
    local exclude_dir="$BATS_TEST_TMPDIR/sanitized-bin"
    mkdir -p "$exclude_dir"
    local excluded=("$@")
    for src_dir in /usr/bin /usr/local/bin /usr/sbin /bin; do
        for bin_path in "$src_dir"/*; do
            local name
            name="$(basename "$bin_path")"
            local skip=false
            for exc in "${excluded[@]}"; do
                [ "$name" = "$exc" ] && skip=true && break
            done
            $skip && continue
            [ -f "$bin_path" ] && [ -x "$bin_path" ] || continue
            ln -sf "$bin_path" "$exclude_dir/$name" 2>/dev/null || true
        done
    done
    printf '%s' "$FAKE_BIN_DIR:$exclude_dir"
}

@test "missing gh CLI with GH_TOKEN set emits FOUNDRY_BOOTSTRAP_FAILED stage=auth and exits non-zero" {
    set_required_env
    make_fake_git

    # GH_TOKEN set but gh absent: build a PATH that excludes the gh binary so
    # command -v gh finds nothing even when gh is installed on the test host.
    export GH_TOKEN="test-gh-token"
    local safe_path
    safe_path="$(make_sanitized_path_without gh)"

    run env PATH="$safe_path" bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=auth"* ]]
}

@test "missing glab CLI with GITLAB_TOKEN set emits FOUNDRY_BOOTSTRAP_FAILED stage=auth and exits non-zero" {
    set_required_env
    make_fake_git

    # GITLAB_TOKEN set but glab absent: build a PATH that excludes the glab binary.
    export GITLAB_TOKEN="test-gitlab-token"
    local safe_path
    safe_path="$(make_sanitized_path_without glab)"

    run env PATH="$safe_path" bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=auth"* ]]
}

# Write a fake gh that exits 0 (CLI present, auth succeeds)
make_fake_gh_success() {
    cat > "$FAKE_BIN_DIR/gh" <<'EOF'
#!/bin/bash
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/gh"
}

@test "gh CLI present with GH_TOKEN: no bootstrap failure and claude is invoked" {
    set_required_env
    make_fake_git
    make_fake_claude
    make_fake_gh_success

    export GH_TOKEN="test-gh-token"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$output" != *"FOUNDRY_BOOTSTRAP_FAILED"* ]]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
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

# Write a fake claude that records the value of TESTCONTAINERS_HOST_OVERRIDE (empty string
# if unset) at invocation time into a marker file so the test can assert it was cleared.
make_fake_claude_record_testcontainers_host() {
    local override_file="$BATS_TEST_TMPDIR/claude_testcontainers_host"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
# Fake claude: record TESTCONTAINERS_HOST_OVERRIDE (empty if unset) and exit 0
printf '%s' "\${TESTCONTAINERS_HOST_OVERRIDE:-}" > "${override_file}"
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

@test "degraded path (dockerd fail): ambient TESTCONTAINERS_HOST_OVERRIDE is not passed to claude" {
    set_required_env
    make_fake_git
    make_fake_claude_record_testcontainers_host
    make_fake_dockerd_fail
    make_fake_docker_unhealthy

    # Inject an ambient override that would send Testcontainers to a dead host
    export TESTCONTAINERS_HOST_OVERRIDE="inherited.example"

    run bash "$ENTRYPOINT"

    # Entrypoint must succeed (degraded mode)
    [ "$status" -eq 0 ]

    # claude must have been invoked — marker file must exist
    local override_file="$BATS_TEST_TMPDIR/claude_testcontainers_host"
    [ -f "$override_file" ]

    # The override seen by claude must be empty — not the injected value
    local recorded_override
    recorded_override="$(cat "$override_file")"
    [ -z "$recorded_override" ]
}

# Fake claude that records, one NAME=value line per Testcontainers connection variable
# (value empty if unset), the environment it was invoked with.
make_fake_claude_record_testcontainers_connection_env() {
    local record_file="$BATS_TEST_TMPDIR/claude_testcontainers_env"
    {
        echo '#!/bin/bash'
        local name
        for name in "${TESTCONTAINERS_CONNECTION_VARS[@]}"; do
            echo "printf '%s=%s\\n' '$name' \"\${$name:-}\" >> '$record_file'"
        done
        echo 'exit 0'
    } > "$FAKE_BIN_DIR/claude"
    chmod +x "$FAKE_BIN_DIR/claude"
}

@test "degraded path (dockerd fail): ambient Testcontainers connection variables are not passed to claude" {
    set_required_env
    make_fake_git
    make_fake_claude_record_testcontainers_connection_env
    make_fake_dockerd_fail
    make_fake_docker_unhealthy

    local name
    for name in "${TESTCONTAINERS_CONNECTION_VARS[@]}"; do
        export "$name=inherited.example"
    done

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local record_file="$BATS_TEST_TMPDIR/claude_testcontainers_env"
    [ -f "$record_file" ]
    # Every variable recorded as empty: no line carries an inherited value
    ! grep -q 'inherited.example' "$record_file"
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

@test "auth: credential file in CLAUDE_SECURESTORAGE_CONFIG_DIR with a container-local config dir — entrypoint reaches claude" {
    # Foundry's OAuth wiring (ADR 0090): the shared credential volume is the CLI's
    # credential store only, and CLAUDE_CONFIG_DIR stays unset so ~/.claude is per-container.
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
printf '%s|%s\n' "\${CLAUDE_SECURESTORAGE_CONFIG_DIR:-}" "\${CLAUDE_CONFIG_DIR:-unset}" > "$BATS_TEST_TMPDIR/claude_env"
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
    make_fake_git

    set_required_env_no_auth
    local cred_dir="$BATS_TEST_TMPDIR/claude-credentials"
    mkdir -p "$cred_dir"
    printf '{"token":"fake"}' > "$cred_dir/.credentials.json"
    export CLAUDE_SECURESTORAGE_CONFIG_DIR="$cred_dir"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(head -n1 "$BATS_TEST_TMPDIR/claude_env")" = "$cred_dir|unset" ]
}

@test "auth: CLAUDE_SECURESTORAGE_CONFIG_DIR without a credential file — exits non-zero even when CLAUDE_CONFIG_DIR has one" {
    # The CLI reads credentials from CLAUDE_SECURESTORAGE_CONFIG_DIR when it is set, so a
    # credential file in CLAUDE_CONFIG_DIR would not authenticate it.
    make_fake_git

    set_required_env_no_auth
    local cred_dir="$BATS_TEST_TMPDIR/claude-credentials-empty"
    local config_dir="$BATS_TEST_TMPDIR/claude-config-with-creds"
    mkdir -p "$cred_dir" "$config_dir"
    printf '{"token":"fake"}' > "$config_dir/.credentials.json"
    export CLAUDE_SECURESTORAGE_CONFIG_DIR="$cred_dir"
    export CLAUDE_CONFIG_DIR="$config_dir"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"CLAUDE_SECURESTORAGE_CONFIG_DIR"* ]]
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
# User settings: the entrypoint removes ~/.claude/settings.json at every start
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
    printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}'
    printf '%s\n' '{"is_error":true,"api_error_status":503,"result":"API Error: Service Unavailable","type":"result"}'
    exit 1
else
    # Record whether -c was passed on the resume call
    printf '%s\n' "\$*" > "${resume_args_file}"
    printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}'
    printf '%s\n' '{"is_error":false,"result":"Completed successfully","subtype":"success","type":"result"}'
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
printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}'
printf '%s\n' '{"is_error":true,"api_error_status":503,"result":"API Error: Service Unavailable","type":"result"}'
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
    printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}'
    printf '%s\n' '{"is_error":true,"result":"API Error: 529 Overloaded","type":"result"}'
    exit 1
else
    printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}'
    printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
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
printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}'
printf '%s\n' '{"is_error":true,"result":"some other error","type":"result"}'
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
printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}'
printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
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
    [[ "$args" =~ (^| )-c( |$) ]]
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

# Write a fake claude that emits the "Connection closed mid-response" phrase
# on first call; success on second (-c resume).
make_fake_claude_connection_closed_then_success() {
    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
# Fake claude: connection-closed phrase transient on first call, success on second
count=0
if [ -f "${call_count_file}" ]; then
    count=\$(cat "${call_count_file}")
fi
count=\$((count + 1))
printf '%d' "\$count" > "${call_count_file}"
if [ "\$count" -eq 1 ]; then
    printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}'
    printf '%s\n' '{"is_error":true,"result":"API Error: Connection closed mid-response","type":"result"}'
    exit 1
else
    printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}'
    printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
    exit 0
fi
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

@test "transient retry: phrase 'API Error: Connection closed mid-response' triggers resume (2 calls)" {
    set_required_env
    make_fake_git
    make_fake_claude_connection_closed_then_success

    run bash "$ENTRYPOINT"

    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    [ -f "$call_count_file" ]
    local count
    count="$(cat "$call_count_file")"
    [ "$count" -eq 2 ]
}

@test "transient retry: phrase 'API Error: Connection closed mid-response' resume succeeds — last JSON is success" {
    set_required_env
    make_fake_git
    make_fake_claude_connection_closed_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local last_line
    last_line="$(printf '%s\n' "$output" | grep '^{' | tail -1)"
    [[ "$last_line" == *'"is_error":false'* ]]
    [[ "$last_line" == *'"subtype":"success"'* ]]
}

# Write a fake claude that emits is_error:false with a 5xx api_error_status
# on first call (5xx classification is independent of is_error — mirrors C#).
make_fake_claude_5xx_no_is_error_then_success() {
    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
# Fake claude: 5xx status with is_error:false on first call, success on second
count=0
if [ -f "${call_count_file}" ]; then
    count=\$(cat "${call_count_file}")
fi
count=\$((count + 1))
printf '%d' "\$count" > "${call_count_file}"
if [ "\$count" -eq 1 ]; then
    printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}'
    printf '%s\n' '{"is_error":false,"api_error_status":503,"type":"result"}'
    exit 1
else
    printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"working"}]}}'
    printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
    exit 0
fi
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

@test "transient retry: 5xx api_error_status triggers resume regardless of is_error flag (2 calls)" {
    set_required_env
    make_fake_git
    make_fake_claude_5xx_no_is_error_then_success

    run bash "$ENTRYPOINT"

    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    [ -f "$call_count_file" ]
    local count
    count="$(cat "$call_count_file")"
    [ "$count" -eq 2 ]
}

# ---------------------------------------------------------------------------
# stream-json output tests
# ---------------------------------------------------------------------------

# Write a fake claude that records each invocation's argv (one line per call)
# and emits a minimal stream-json run ending in a success result event.
make_fake_claude_record_args() {
    local args_file="$BATS_TEST_TMPDIR/claude_args"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
printf '%s\n' "\$*" >> "${args_file}"
printf '%s\n' '{"type":"system","subtype":"init","model":"claude-test"}'
printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

# Write a fake claude that emits one event, then waits (up to ~5 s) until that
# event has reached the entrypoint's stdout file ($STREAM_OUT_FILE) before
# emitting the result.  It records "streamed" only when the first line was
# visible while it was still running — i.e. stdout is not buffered until exit.
make_fake_claude_streaming_probe() {
    local marker_file="$BATS_TEST_TMPDIR/claude_streamed"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
printf '%s\n' '{"type":"assistant","message":{"content":[{"type":"text","text":"first-event"}]}}'
for _ in \$(seq 1 50); do
    if grep -q 'first-event' "\${STREAM_OUT_FILE}" 2>/dev/null; then
        echo streamed > "${marker_file}"
        break
    fi
    sleep 0.1
done
printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

# Write a fake claude whose last JSON line is a NON-result event carrying a 5xx
# status after a successful result event — must not trigger a resume.
make_fake_claude_success_then_noisy_event() {
    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
count=0
if [ -f "${call_count_file}" ]; then
    count=\$(cat "${call_count_file}")
fi
count=\$((count + 1))
printf '%d' "\$count" > "${call_count_file}"
printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
printf '%s\n' '{"type":"system","subtype":"task_notification","is_error":true,"api_error_status":503,"summary":"API Error: 529 Overloaded"}'
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

# Write a fake claude whose transient result event is followed by a trailing
# non-result event — the result event still decides, so a resume happens.
make_fake_claude_transient_then_trailing_event() {
    local call_count_file="$BATS_TEST_TMPDIR/claude_call_count"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
count=0
if [ -f "${call_count_file}" ]; then
    count=\$(cat "${call_count_file}")
fi
count=\$((count + 1))
printf '%d' "\$count" > "${call_count_file}"
if [ "\$count" -eq 1 ]; then
    printf '%s\n' '{"is_error":true,"api_error_status":503,"result":"API Error","type":"result"}'
    printf '%s\n' '{"type":"system","subtype":"hook_response","outcome":"success"}'
    exit 1
fi
printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

@test "stream-json: claude is invoked with --output-format stream-json and --verbose" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local args
    args="$(head -n 1 "$BATS_TEST_TMPDIR/claude_args")"
    [[ "$args" == *"--output-format stream-json"* ]]
    [[ "$args" =~ (^| )--verbose( |$) ]]
    [[ "$args" != *"--output-format json"* ]]
}

@test "stream-json: transient-retry resume uses --output-format stream-json and --verbose" {
    set_required_env
    make_fake_git
    make_fake_claude_transient_then_success

    run bash "$ENTRYPOINT"

    local args
    args="$(cat "$BATS_TEST_TMPDIR/claude_resume_args")"
    [[ "$args" =~ (^| )-c( |$) ]]
    [[ "$args" == *"--output-format stream-json"* ]]
    [[ "$args" =~ (^| )--verbose( |$) ]]
}

@test "max-turns: the first claude run is given a 300-turn budget" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local args
    args="$(head -n 1 "$BATS_TEST_TMPDIR/claude_args")"
    [[ "$args" =~ (^| )--max-turns\ 300( |$) ]]
}

@test "max-turns: the transient-retry resume gets the same 300-turn budget" {
    set_required_env
    make_fake_git
    make_fake_claude_transient_then_success

    run bash "$ENTRYPOINT"

    local args
    args="$(cat "$BATS_TEST_TMPDIR/claude_resume_args")"
    [[ "$args" =~ (^| )-c( |$) ]]
    [[ "$args" =~ (^| )--max-turns\ 300( |$) ]]
}

@test "stream-json: every claude invocation that passes stream-json also passes --verbose" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args

    run bash "$ENTRYPOINT"

    [ -s "$BATS_TEST_TMPDIR/claude_args" ]
    while IFS= read -r args; do
        if [[ "$args" == *"stream-json"* ]]; then
            [[ "$args" =~ (^| )--verbose( |$) ]]
        fi
    done < "$BATS_TEST_TMPDIR/claude_args"
}

@test "stream-json: stdout reaches the container log while claude is still running" {
    set_required_env
    make_fake_git
    make_fake_claude_streaming_probe
    export STREAM_OUT_FILE="$BATS_TEST_TMPDIR/entrypoint_stdout"

    bash "$ENTRYPOINT" > "$STREAM_OUT_FILE" 2>/dev/null

    [ -f "$BATS_TEST_TMPDIR/claude_streamed" ]
    grep -q '"type":"result"' "$STREAM_OUT_FILE"
}

@test "stream-json: all events are passed through to stdout in order" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local json_lines
    json_lines="$(printf '%s\n' "$output" | grep '^{')"
    [ "$(printf '%s\n' "$json_lines" | head -n 1)" = '{"type":"system","subtype":"init","model":"claude-test"}' ]
    [[ "$(printf '%s\n' "$json_lines" | tail -n 1)" == *'"type":"result"'* ]]
}

@test "stream-json: a non-result event after a successful result does NOT trigger resume" {
    set_required_env
    make_fake_git
    make_fake_claude_success_then_noisy_event

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(cat "$BATS_TEST_TMPDIR/claude_call_count")" -eq 1 ]
}

@test "stream-json: a transient result followed by a trailing non-result event still triggers resume" {
    set_required_env
    make_fake_git
    make_fake_claude_transient_then_trailing_event

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(cat "$BATS_TEST_TMPDIR/claude_call_count")" -eq 2 ]
}

# ---------------------------------------------------------------------------

@test "user settings: a stale read-only settings.json on the OAuth volume is removed at start" {
    make_fake_claude
    make_fake_git

    # Simulate the shared credential volume after an earlier worker (or a worker that
    # injected its own settings): a read-only settings.json next to the credentials.
    local cred_dir="$HOME/.claude"
    printf '{"permissions":{"allow":["Bash(*)"]}}' > "$cred_dir/settings.json"
    chmod 444 "$cred_dir/settings.json"
    printf '{"token":"fake"}' > "$cred_dir/.credentials.json"

    export CLAUDE_CONFIG_DIR="$cred_dir"
    unset ANTHROPIC_API_KEY
    export CLONE_URL="https://github.com/example/repo"
    export GIT_PAT="test-pat"
    export WORKER_PROMPT="test-prompt"
    export SYSTEM_PROMPT="test-system"
    export ISSUE_NUMBER="1"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
    [ ! -e "$cred_dir/settings.json" ]
}

@test "user settings: the entrypoint never writes ~/.claude/settings.json" {
    make_fake_claude
    make_fake_git
    set_required_env
    export CLAUDE_SETTINGS_JSON='{"permissions":{"deny":["Bash(git push --force:*)"]}}'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ ! -e "$HOME/.claude/settings.json" ]
}

@test "user settings: settings.json in a separate CLAUDE_CONFIG_DIR is removed too" {
    make_fake_claude
    make_fake_git
    set_required_env
    local config_dir="$BATS_TEST_TMPDIR/config-dir"
    mkdir -p "$config_dir"
    printf '{}' > "$config_dir/settings.json"
    export CLAUDE_CONFIG_DIR="$config_dir"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ ! -e "$config_dir/settings.json" ]
}

# ---------------------------------------------------------------------------
# Managed settings: non-root invariant check
# ---------------------------------------------------------------------------
#
# As non-root the entrypoint first verifies /etc/claude-code/managed-settings.json
# exists, is owned by uid 0 and has no group/other write bit.  The fake stat from
# setup() reports FAKE_MANAGED_STAT for that path.

@test "managed settings: a valid root-owned 0644 file lets the entrypoint reach claude" {
    make_fake_claude
    make_fake_git
    set_required_env
    export FAKE_MANAGED_STAT="0 644"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
    [[ "$output" != *"FOUNDRY_BOOTSTRAP_FAILED"* ]]
}

@test "managed settings: a missing file fails bootstrap and tells the operator to rebuild" {
    make_fake_claude
    make_fake_git
    set_required_env
    export FAKE_MANAGED_STAT="missing"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=managed-settings detail="*"rebuild the worker image"* ]]
}

@test "managed settings: a file not owned by root fails bootstrap" {
    make_fake_claude
    make_fake_git
    set_required_env
    export FAKE_MANAGED_STAT="1000 644"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=managed-settings detail="*"not owned by root"*"rebuild the worker image"* ]]
}

@test "managed settings: a group-writable file fails bootstrap" {
    make_fake_claude
    make_fake_git
    set_required_env
    export FAKE_MANAGED_STAT="0 664"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=managed-settings detail="*"writable"* ]]
}

@test "managed settings: an other-writable file fails bootstrap" {
    make_fake_claude
    make_fake_git
    set_required_env
    export FAKE_MANAGED_STAT="0 646"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=managed-settings"* ]]
}

@test "managed settings: the invariant check runs before any other work" {
    make_fake_claude
    make_fake_git
    # No auth and no required env at all: the managed-settings failure must come first.
    unset ANTHROPIC_API_KEY CLONE_URL GIT_PAT WORKER_PROMPT SYSTEM_PROMPT ISSUE_NUMBER
    export FAKE_MANAGED_STAT="missing"
    printf '{}' > "$HOME/.claude/settings.json"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=managed-settings"* ]]
    [[ "$output" != *"Authentication required"* ]]
    # Nothing ran after the failed check — not even the user-settings cleanup.
    [ -e "$HOME/.claude/settings.json" ]
}

# ---------------------------------------------------------------------------
# SYSTEM_PROMPT byte-size guard tests
# ---------------------------------------------------------------------------
#
# The kernel MAX_ARG_STRLEN (131071 bytes) limits individual env/argv strings
# passed via execve. Env vars at or above this size cannot be passed to a child
# process, so tests must fake `wc` to simulate oversized counts without needing
# to export a physically oversized string.

# Helper: write a fake `wc` that returns a controlled byte count for stdin.
# The fake responds to `wc -c` by printing the value from WC_FAKE_BYTES.
# All other wc invocations fall through to the real binary.
make_fake_wc_bytes() {
    local fake_count="$1"
    cat > "$FAKE_BIN_DIR/wc" <<EOF
#!/bin/bash
if [[ "\${1:-}" == "-c" ]]; then
    printf '%s\n' "${fake_count}"
else
    exec /usr/bin/wc "\$@"
fi
EOF
    chmod +x "$FAKE_BIN_DIR/wc"
}

@test "oversized SYSTEM_PROMPT exits non-zero before reaching claude" {
    make_fake_claude
    make_fake_git
    # Fake wc -c to report 131071 bytes — at the ceiling
    make_fake_wc_bytes 131071
    set_required_env

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "oversized SYSTEM_PROMPT emits diagnostic naming the ceiling and actual size" {
    make_fake_claude
    make_fake_git
    # Fake wc -c to report 145000 bytes — clearly over the ceiling
    make_fake_wc_bytes 145000
    set_required_env

    run bash "$ENTRYPOINT"

    # Diagnostic must name both the ceiling (131071) and the actual byte size
    [[ "$output" == *"131071"* ]]
    [[ "$output" == *"145000"* ]]
}

@test "SYSTEM_PROMPT byte-count uses bytes not chars: wc -c called even in UTF-8 locale" {
    make_fake_claude
    make_fake_git
    # Fake wc -c to report 196608 bytes — 65536 three-byte '€' chars.
    # In a UTF-8 locale ${#SYSTEM_PROMPT} would return 65536 (chars, under the limit)
    # while wc -c returns 196608 (bytes, over the limit).
    # The guard must use the byte count to reject this.
    make_fake_wc_bytes 196608
    set_required_env

    run env LC_ALL=C.utf8 bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"131071"* ]]
}

@test "SYSTEM_PROMPT at one byte under the limit passes the guard and reaches claude" {
    make_fake_claude
    make_fake_git
    # Fake wc -c to report 131070 bytes — one below the 131071 ceiling
    make_fake_wc_bytes 131070
    set_required_env

    run bash "$ENTRYPOINT"

    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
}

# ---------------------------------------------------------------------------
# Managed settings render tests
# ---------------------------------------------------------------------------
#
# render_managed_settings renders CLAUDE_SETTINGS_JSON to the path it is given.
# Production passes /etc/claude-code/managed-settings.json from the root phase;
# these tests load the function with awk and render to a per-test temp path.

# Loads render_managed_settings and the helpers it calls into the current shell.
load_render_function() {
    local name func_body
    for name in fail_bootstrap render_managed_settings; do
        func_body="$(awk "/^${name}\\(\\)/,/^\\}/" "$ENTRYPOINT")"
        [ -n "$func_body" ]
        eval "$func_body"
    done
}

# Prepare the render function, the generated settings and the output path.
set_managed_render_env() {
    load_render_function
    MANAGED_DIR="$BATS_TEST_TMPDIR/etc/claude-code"
    MANAGED_FILE="$MANAGED_DIR/managed-settings.json"
    export CLAUDE_SETTINGS_JSON='{"permissions":{"deny":["Bash(git push --force:*)"]},"model":"claude-opus-5-5"}'
}

written_settings() {
    cat "$MANAGED_FILE"
}

@test "managed render: writes CLAUDE_SETTINGS_JSON unchanged" {
    set_managed_render_env

    run render_managed_settings "$MANAGED_FILE"

    [ "$status" -eq 0 ]
    [ "$(written_settings | jq -c '.')" = "$CLAUDE_SETTINGS_JSON" ]
}

@test "managed render: the directory is mode 0755 and the file mode 0644" {
    set_managed_render_env

    run render_managed_settings "$MANAGED_FILE"

    [ "$status" -eq 0 ]
    [ "$(stat -c '%a' "$MANAGED_DIR")" = "755" ]
    [ "$(stat -c '%a' "$MANAGED_FILE")" = "644" ]
}

@test "managed render: an existing read-only file is replaced and no temp file is left behind" {
    set_managed_render_env
    mkdir -p "$MANAGED_DIR"
    printf '{"old":"value"}' > "$MANAGED_FILE"
    chmod 444 "$MANAGED_FILE"

    run render_managed_settings "$MANAGED_FILE"

    [ "$status" -eq 0 ]
    [ "$(written_settings | jq -c '.')" = "$CLAUDE_SETTINGS_JSON" ]
    [ "$(ls -A "$MANAGED_DIR")" = "managed-settings.json" ]
}

@test "managed render: an empty CLAUDE_SETTINGS_JSON fails bootstrap and writes nothing" {
    set_managed_render_env
    export CLAUDE_SETTINGS_JSON=""

    run render_managed_settings "$MANAGED_FILE"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=managed-settings"* ]]
    [ ! -e "$MANAGED_FILE" ]
}

@test "managed render: a missing CLAUDE_SETTINGS_JSON fails bootstrap" {
    set_managed_render_env
    unset CLAUDE_SETTINGS_JSON

    run render_managed_settings "$MANAGED_FILE"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=managed-settings"* ]]
    [ ! -e "$MANAGED_FILE" ]
}

@test "managed render: a CLAUDE_SETTINGS_JSON that is not a JSON object fails bootstrap" {
    set_managed_render_env
    export CLAUDE_SETTINGS_JSON='not json'

    run render_managed_settings "$MANAGED_FILE"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=managed-settings"* ]]
    [ ! -e "$MANAGED_FILE" ]
}

@test "managed render: never carries an allowManagedHooksOnly key" {
    set_managed_render_env
    export CLAUDE_SETTINGS_JSON='{"allowManagedHooksOnly":true,"model":"claude-opus-5-5"}'

    run render_managed_settings "$MANAGED_FILE"

    [ "$status" -eq 0 ]
    [ "$(written_settings | jq -r 'has("allowManagedHooksOnly")')" = "false" ]
    [ "$(written_settings | jq -r '.model')" = "claude-opus-5-5" ]
}

# ---------------------------------------------------------------------------
# C# LSP pre-restore (prepare_csharp_lsp): the csharp-lsp plugin is loaded only
# after every restore target restores cleanly; anything else fails closed to
# "no LSP" without failing the bootstrap.
# ---------------------------------------------------------------------------

# Loads prepare_csharp_lsp from entrypoint.sh and defines the constants the
# entrypoint declares at its call site (the timeout is shortened by tests).
load_prepare_csharp_lsp() {
    local func_body
    func_body="$(awk '/^prepare_csharp_lsp\(\)/,/^\}/' "$ENTRYPOINT")"
    eval "$func_body"
    CSHARP_LSP_RESTORE_TIMEOUT_SECONDS=300
    CSHARP_LSP_PLUGIN_DIR=/opt/claude-plugins/csharp-lsp
}

# Fake csharp-ls: only its presence on PATH matters.
make_fake_csharp_ls() {
    cat > "$FAKE_BIN_DIR/csharp-ls" <<'EOF'
#!/bin/bash
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/csharp-ls"
}

# Fake dotnet: appends "<args>" to $BATS_TEST_TMPDIR/dotnet_calls and
# "dotnet GIT_PAT=<value|unset>" to $BATS_TEST_TMPDIR/order.  Behaviour is
# read at run time from FAKE_DOTNET_FAIL_MATCH (exit 1 when the last argument
# contains it) and FAKE_DOTNET_SLEEP (seconds to sleep before exiting).
make_fake_dotnet() {
    local calls_file="$BATS_TEST_TMPDIR/dotnet_calls"
    local order_file="$BATS_TEST_TMPDIR/order"
    cat > "$FAKE_BIN_DIR/dotnet" <<EOF
#!/bin/bash
printf '%s\n' "\$*" >> "${calls_file}"
printf 'dotnet GIT_PAT=%s\n' "\${GIT_PAT:-unset}" >> "${order_file}"
if [[ -n "\${FAKE_DOTNET_SLEEP:-}" ]]; then
    sleep "\${FAKE_DOTNET_SLEEP}"
fi
if [[ -n "\${FAKE_DOTNET_FAIL_MATCH:-}" && "\${!#}" == *"\${FAKE_DOTNET_FAIL_MATCH}"* ]]; then
    echo "error NU1301: Unable to load the service index" >&2
    exit 1
fi
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/dotnet"
}

# Fake find that redirects a search rooted at the hardcoded clone target
# /workspace to $FAKE_WORKSPACE_DIR, so full-entrypoint tests can supply a
# fixture tree without touching the runner's /workspace.
make_fake_find_workspace() {
    local real_find
    real_find="$(command -v find)"
    cat > "$FAKE_BIN_DIR/find" <<EOF
#!/bin/bash
if [[ "\${1:-}" == "/workspace" && -n "\${FAKE_WORKSPACE_DIR:-}" ]]; then
    shift
    exec "${real_find}" "\${FAKE_WORKSPACE_DIR}" "\$@"
fi
exec "${real_find}" "\$@"
EOF
    chmod +x "$FAKE_BIN_DIR/find"
}

# Creates empty files (and their parent directories) under $1.
make_fixture_tree() {
    local root="$1"
    shift
    local rel
    for rel in "$@"; do
        mkdir -p "$root/$(dirname "$rel")"
        : > "$root/$rel"
    done
}

# Writes the restore targets recorded by the fake dotnet, one per line, as
# paths relative to $1 (dotnet is invoked as "dotnet restore <path>").
restored_targets() {
    local root="$1"
    [ -f "$BATS_TEST_TMPDIR/dotnet_calls" ] || return 0
    sed -e 's/^restore //' -e "s#^${root}/##" "$BATS_TEST_TMPDIR/dotnet_calls"
}

# Fake claude that appends every invocation's argv to $BATS_TEST_TMPDIR/claude_args
# and $BATS_TEST_TMPDIR/order: a transient 5xx result on the first call (so the
# entrypoint resumes via claude -c), success on the second.
make_fake_claude_record_args_transient_then_success() {
    local args_file="$BATS_TEST_TMPDIR/claude_args"
    local order_file="$BATS_TEST_TMPDIR/order"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
printf '%s\n' "\$*" >> "${args_file}"
echo claude >> "${order_file}"
if [ "\$(wc -l < "${args_file}")" -eq 1 ]; then
    printf '%s\n' '{"is_error":true,"api_error_status":503,"result":"API Error: Service Unavailable","type":"result"}'
    exit 1
fi
printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

@test "csharp lsp: a solution in a subfolder is restored and the plugin is enabled" {
    load_prepare_csharp_lsp
    make_fake_csharp_ls
    make_fake_dotnet
    local repo="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$repo" src/App.slnx src/App/App.csproj node_modules/pkg/Ignored.sln

    prepare_csharp_lsp "$repo" 2> "$BATS_TEST_TMPDIR/log"

    [ "$(restored_targets "$repo")" = "src/App.slnx" ]
    [ "${CSHARP_LSP_PLUGIN_ARGS[*]}" = "--plugin-dir /opt/claude-plugins/csharp-lsp" ]
    grep -qx "C# LSP: enabled" "$BATS_TEST_TMPDIR/log"
}

@test "csharp lsp: every solution is restored when there are several" {
    load_prepare_csharp_lsp
    make_fake_csharp_ls
    make_fake_dotnet
    local repo="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$repo" Root.sln tools/Tools.slnx tools/Tool/Tool.csproj

    prepare_csharp_lsp "$repo" 2> "$BATS_TEST_TMPDIR/log"

    [ "$(restored_targets "$repo")" = "$(printf '%s\n' Root.sln tools/Tools.slnx)" ]
    [ "${#CSHARP_LSP_PLUGIN_ARGS[@]}" -eq 2 ]
    grep -qx "C# LSP: enabled" "$BATS_TEST_TMPDIR/log"
}

@test "csharp lsp: without solutions every csproj/fsproj outside node_modules is restored" {
    load_prepare_csharp_lsp
    make_fake_csharp_ls
    make_fake_dotnet
    local repo="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$repo" a/A.csproj b/nested/B.fsproj node_modules/x/X.csproj web/node_modules/y/Y.fsproj

    prepare_csharp_lsp "$repo" 2> "$BATS_TEST_TMPDIR/log"

    [ "$(restored_targets "$repo")" = "$(printf '%s\n' a/A.csproj b/nested/B.fsproj)" ]
    [ "${CSHARP_LSP_PLUGIN_ARGS[*]}" = "--plugin-dir /opt/claude-plugins/csharp-lsp" ]
    grep -qx "C# LSP: enabled" "$BATS_TEST_TMPDIR/log"
}

@test "csharp lsp: a failing restore disables the plugin and names the reason" {
    load_prepare_csharp_lsp
    make_fake_csharp_ls
    make_fake_dotnet
    local repo="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$repo" A.sln B.sln
    export FAKE_DOTNET_FAIL_MATCH="A.sln"

    prepare_csharp_lsp "$repo" 2> "$BATS_TEST_TMPDIR/log"

    [ "${#CSHARP_LSP_PLUGIN_ARGS[@]}" -eq 0 ]
    grep -q "^C# LSP: disabled (dotnet restore failed for A.sln" "$BATS_TEST_TMPDIR/log"
    ! grep -q "C# LSP: enabled" "$BATS_TEST_TMPDIR/log"
    ! grep -q "FOUNDRY_BOOTSTRAP_FAILED" "$BATS_TEST_TMPDIR/log"
}

@test "csharp lsp: a restore that exceeds the overall timeout disables the plugin" {
    load_prepare_csharp_lsp
    CSHARP_LSP_RESTORE_TIMEOUT_SECONDS=1
    make_fake_csharp_ls
    make_fake_dotnet
    local repo="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$repo" App.sln
    export FAKE_DOTNET_SLEEP=10

    local started=$SECONDS
    prepare_csharp_lsp "$repo" 2> "$BATS_TEST_TMPDIR/log"

    [ $(( SECONDS - started )) -lt 8 ]
    [ "${#CSHARP_LSP_PLUGIN_ARGS[@]}" -eq 0 ]
    grep -qx "C# LSP: disabled (restore exceeded 1s)" "$BATS_TEST_TMPDIR/log"
}

@test "csharp lsp: the timeout bounds all restores together, not each one" {
    load_prepare_csharp_lsp
    CSHARP_LSP_RESTORE_TIMEOUT_SECONDS=3
    make_fake_csharp_ls
    make_fake_dotnet
    local repo="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$repo" A.sln B.sln C.sln
    export FAKE_DOTNET_SLEEP=2

    prepare_csharp_lsp "$repo" 2> "$BATS_TEST_TMPDIR/log"

    [ "${#CSHARP_LSP_PLUGIN_ARGS[@]}" -eq 0 ]
    grep -qx "C# LSP: disabled (restore exceeded 3s)" "$BATS_TEST_TMPDIR/log"
    [ "$(wc -l < "$BATS_TEST_TMPDIR/dotnet_calls")" -lt 3 ]
}

@test "csharp lsp: no solution or project files means no restore and no plugin" {
    load_prepare_csharp_lsp
    make_fake_csharp_ls
    make_fake_dotnet
    local repo="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$repo" README.md node_modules/x/X.csproj

    prepare_csharp_lsp "$repo" 2> "$BATS_TEST_TMPDIR/log"

    [ ! -f "$BATS_TEST_TMPDIR/dotnet_calls" ]
    [ "${#CSHARP_LSP_PLUGIN_ARGS[@]}" -eq 0 ]
    grep -qx "C# LSP: disabled (no solution or project files)" "$BATS_TEST_TMPDIR/log"
}

@test "csharp lsp: csharp-ls missing from PATH means no restore and no plugin" {
    load_prepare_csharp_lsp
    make_fake_dotnet
    local repo="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$repo" App.sln
    local safe_path
    safe_path="$(make_sanitized_path_without csharp-ls)"

    PATH="$safe_path" prepare_csharp_lsp "$repo" 2> "$BATS_TEST_TMPDIR/log"

    [ ! -f "$BATS_TEST_TMPDIR/dotnet_calls" ]
    [ "${#CSHARP_LSP_PLUGIN_ARGS[@]}" -eq 0 ]
    grep -qx "C# LSP: disabled (csharp-ls not on PATH)" "$BATS_TEST_TMPDIR/log"
}

@test "csharp lsp entrypoint: a clean restore passes --plugin-dir to both claude -p and the claude -c retry" {
    set_required_env
    make_fake_git
    make_fake_csharp_ls
    make_fake_dotnet
    make_fake_find_workspace
    make_fake_claude_record_args_transient_then_success
    export FAKE_WORKSPACE_DIR="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$FAKE_WORKSPACE_DIR" src/App.slnx

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$output" == *"C# LSP: enabled"* ]]
    [ "$(wc -l < "$BATS_TEST_TMPDIR/claude_args")" -eq 2 ]
    local first resume
    first="$(sed -n 1p "$BATS_TEST_TMPDIR/claude_args")"
    resume="$(sed -n 2p "$BATS_TEST_TMPDIR/claude_args")"
    [[ "$first" == "-p "* ]]
    [[ "$resume" == "-c -p "* ]]
    [[ "$first" == *"--plugin-dir /opt/claude-plugins/csharp-lsp"* ]]
    [[ "$resume" == *"--plugin-dir /opt/claude-plugins/csharp-lsp"* ]]
    # The retry reuses the decision: the repo is restored exactly once.
    [ "$(wc -l < "$BATS_TEST_TMPDIR/dotnet_calls")" -eq 1 ]
}

@test "csharp lsp entrypoint: a failed restore passes the csharp-lsp plugin to neither claude invocation and the run continues" {
    set_required_env
    make_fake_git
    make_fake_csharp_ls
    make_fake_dotnet
    make_fake_find_workspace
    make_fake_claude_record_args_transient_then_success
    export FAKE_WORKSPACE_DIR="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$FAKE_WORKSPACE_DIR" App.sln
    export FAKE_DOTNET_FAIL_MATCH="App.sln"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$output" == *"C# LSP: disabled (dotnet restore failed for"* ]]
    [[ "$output" != *"FOUNDRY_BOOTSTRAP_FAILED"* ]]
    [ "$(wc -l < "$BATS_TEST_TMPDIR/claude_args")" -eq 2 ]
    ! grep -q -- "--plugin-dir /opt/claude-plugins/csharp-lsp" "$BATS_TEST_TMPDIR/claude_args"
}

@test "csharp lsp entrypoint: without csharp-ls on PATH no restore runs and the csharp-lsp plugin is not passed" {
    set_required_env
    make_fake_git
    make_fake_dotnet
    make_fake_find_workspace
    make_fake_claude_record_args_transient_then_success
    export FAKE_WORKSPACE_DIR="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$FAKE_WORKSPACE_DIR" App.sln
    local safe_path
    safe_path="$(make_sanitized_path_without csharp-ls)"

    run env PATH="$safe_path" bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$output" == *"C# LSP: disabled (csharp-ls not on PATH)"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/dotnet_calls" ]
    ! grep -q -- "--plugin-dir /opt/claude-plugins/csharp-lsp" "$BATS_TEST_TMPDIR/claude_args"
}

@test "csharp lsp entrypoint: the restore runs after GIT_PAT is unset and gh setup, immediately before claude" {
    set_required_env
    make_fake_git
    make_fake_csharp_ls
    make_fake_dotnet
    make_fake_find_workspace
    make_fake_claude_record_args_transient_then_success
    export FAKE_WORKSPACE_DIR="$BATS_TEST_TMPDIR/repo"
    make_fixture_tree "$FAKE_WORKSPACE_DIR" App.sln
    export GH_TOKEN="test-gh-token"
    local order_file="$BATS_TEST_TMPDIR/order"
    cat > "$FAKE_BIN_DIR/gh" <<EOF
#!/bin/bash
echo gh >> "${order_file}"
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/gh"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(cat "$order_file")" = "$(printf '%s\n' gh 'dotnet GIT_PAT=unset' claude claude)" ]
}

# ---------------------------------------------------------------------------
# Worker roles (ADR 0095): the run's role and the shared plugin are loaded with
# --plugin-dir; a role the image does not carry fails bootstrap at stage=role.
# ---------------------------------------------------------------------------

@test "role: claude loads the run's role plugin and the shared plugin with --plugin-dir" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local args
    args="$(head -n 1 "$BATS_TEST_TMPDIR/claude_args")"
    [[ "$args" == *"--plugin-dir $FOUNDRY_ROLES_DIR/implement --plugin-dir $FOUNDRY_ROLES_DIR/shared"* ]]
}

@test "role: the transient-error resume loads the same role plugins" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local resume_args
    resume_args="$(sed -n 2p "$BATS_TEST_TMPDIR/claude_args")"
    [[ "$resume_args" =~ (^| )-c( |$) ]]
    [[ "$resume_args" == *"--plugin-dir $FOUNDRY_ROLES_DIR/implement --plugin-dir $FOUNDRY_ROLES_DIR/shared"* ]]
}

@test "role: a role with no folder in the image fails bootstrap at stage=role before cloning" {
    set_required_env
    make_fake_git
    make_fake_claude
    export WORKER_ROLE="review"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=role"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "role: a missing shared plugin fails bootstrap at stage=role" {
    set_required_env
    make_fake_git
    make_fake_claude
    rm -rf "$FOUNDRY_ROLES_DIR/shared"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=role"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "role: a role name that is not a plain slug fails bootstrap at stage=role" {
    set_required_env
    make_fake_git
    make_fake_claude
    make_role_fixture "$BATS_TEST_TMPDIR" outside
    export WORKER_ROLE="../outside"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=role"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "role: the shared plugin cannot be dispatched as a role" {
    set_required_env
    make_fake_git
    make_fake_claude
    export WORKER_ROLE="shared"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=role"* ]]
}

@test "role: a missing WORKER_ROLE stops the entrypoint before cloning" {
    set_required_env
    make_fake_git
    make_fake_claude
    unset WORKER_ROLE

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"WORKER_ROLE is required"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

# ---------------------------------------------------------------------------
# Verdict schemas (ADR 0094): a role's optional verdict.schema.json is passed to
# both claude invocations with --json-schema, with the shared $defs merged in.
# ---------------------------------------------------------------------------

# write_verdict_fixture — gives the implement fixture role a verdict schema that
# references the shared questions definition, and writes that definition.
write_verdict_fixture() {
    printf '%s\n' '{"type":"object","required":["outcome"],"properties":{"outcome":{"enum":["done","needs_input"]},"questions":{"$ref":"#/$defs/questions"}}}' \
        > "$FOUNDRY_ROLES_DIR/implement/verdict.schema.json"
    printf '%s\n' '{"$defs":{"questions":{"type":"array","minItems":1}}}' \
        > "$FOUNDRY_ROLES_DIR/shared/verdict.defs.json"
}

# The schema the entrypoint must pass: the role schema with the shared $defs merged in.
expected_verdict_schema() {
    jq -c '.["$defs"] = {"questions":{"type":"array","minItems":1}}' "$FOUNDRY_ROLES_DIR/implement/verdict.schema.json"
}

@test "verdict: claude receives the role's verdict schema with the shared definitions via --json-schema" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args
    write_verdict_fixture

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local args
    args="$(head -n 1 "$BATS_TEST_TMPDIR/claude_args")"
    [[ "$args" == *"--json-schema $(expected_verdict_schema)"* ]]
    [[ "$args" == *"--output-format stream-json --verbose"* ]]
}

@test "verdict: the transient-error resume passes the same --json-schema" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args_transient_then_success
    write_verdict_fixture

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local resume_args
    resume_args="$(sed -n 2p "$BATS_TEST_TMPDIR/claude_args")"
    [[ "$resume_args" =~ (^| )-c( |$) ]]
    [[ "$resume_args" == *"--json-schema $(expected_verdict_schema)"* ]]
    [[ "$resume_args" == *"--output-format stream-json --verbose"* ]]
}

@test "verdict: a role without verdict.schema.json gets no --json-schema" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(wc -l < "$BATS_TEST_TMPDIR/claude_args")" -eq 2 ]
    ! grep -q -- "--json-schema" "$BATS_TEST_TMPDIR/claude_args"
}

@test "verdict: a role schema without shared definitions is passed as-is" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args
    printf '%s\n' '{"type":"object","required":["outcome"]}' > "$FOUNDRY_ROLES_DIR/implement/verdict.schema.json"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$(head -n 1 "$BATS_TEST_TMPDIR/claude_args")" == *'--json-schema {"type":"object","required":["outcome"]}'* ]]
}

@test "verdict: a verdict schema that is not valid JSON fails bootstrap at stage=role before cloning" {
    set_required_env
    make_fake_git
    make_fake_claude
    printf '%s\n' '{"type":' > "$FOUNDRY_ROLES_DIR/implement/verdict.schema.json"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=role"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "verdict: shared verdict definitions that are not valid JSON fail bootstrap at stage=role" {
    set_required_env
    make_fake_git
    make_fake_claude
    write_verdict_fixture
    printf '%s\n' 'not json' > "$FOUNDRY_ROLES_DIR/shared/verdict.defs.json"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=role"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}
