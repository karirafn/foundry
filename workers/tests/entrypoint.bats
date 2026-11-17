#!/usr/bin/env bats
# Tests for entrypoint.sh.
# All fake binaries are written into a per-test temp dir ($FAKE_BIN_DIR) so the
# committed helpers/ directory is never modified and tests are safe under parallel
# execution.

bats_require_minimum_version 1.5.0

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

# Prints $PATH with every directory that holds <name> replaced by a shadow directory
# of symlinks to all its other entries (dotfiles included), so `command -v <name>`
# no longer finds it while every sibling tool still resolves. Other entries (also
# empty ones and missing directories) are kept as they are, in order. Shadows go
# under the current test's temp dir, or the file's when called from setup_file.
# A plain fake in $FAKE_BIN_DIR cannot hide a real binary: command -v skips a
# non-executable file and reports a function, so the real directory must leave PATH.
shadow_from_path() (
    local name="$1"
    shopt -s nullglob dotglob
    local shadow_root
    shadow_root="$(mktemp -d "${BATS_TEST_TMPDIR:-$BATS_FILE_TMPDIR}/path-shadow.XXXXXX")" || return 1
    local rest="$PATH" dir result="" separator="" index=0
    while :; do
        dir="${rest%%:*}"
        if [ -n "$dir" ] && { [ -e "$dir/$name" ] || [ -L "$dir/$name" ]; }; then
            local source_dir shadow_dir="$shadow_root/$index" entry
            source_dir="$(cd "$dir" && pwd)" || return 1
            local entries=()
            for entry in "$source_dir"/*; do
                [ "${entry##*/}" = "$name" ] || entries+=("$entry")
            done
            mkdir "$shadow_dir" || return 1
            if [ "${#entries[@]}" -gt 0 ]; then
                ln -s "${entries[@]}" "$shadow_dir/" || return 1
            fi
            dir="$shadow_dir"
        fi
        result+="$separator$dir"
        separator=":"
        index=$((index + 1))
        [[ "$rest" == *:* ]] || break
        rest="${rest#*:}"
    done
    printf '%s\n' "$result"
)

setup_file() {
    # Worker images ship the real dockerd-rootless.sh; tests that assume it is absent
    # (as on CI) would otherwise start real rootless daemons and exhaust the worker's
    # PID limit. Shadow it once per file so every test sees the CI host.
    local shadowed_path
    shadowed_path="$(shadow_from_path dockerd-rootless.sh)" || return 1
    export PATH="$shadowed_path"
}

setup() {
    local leaked_dockerd
    if leaked_dockerd="$(command -v dockerd-rootless.sh)"; then
        echo "dockerd-rootless.sh is still on PATH at $leaked_dockerd; shadow_from_path missed it" >&2
        return 1
    fi

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

    # The image bakes the real toolchain report at /opt/foundry/toolchain.sh; inside a live
    # worker it would take seconds per test, so every test gets a fast fake (see make_fake_toolchain).
    make_fake_toolchain 'FOUNDRY_TOOLCHAIN [{"name":"node","version":"22.22.3"}]' 0
    export FOUNDRY_TOOLCHAIN_SCRIPT="$BATS_TEST_TMPDIR/toolchain.sh"

    # Full-entrypoint tests resolve the run's role plugins before cloning. Point the
    # entrypoint at a per-test roles tree holding the implement and shared plugins
    # (the image bakes the real one at /opt/foundry/roles).
    make_role_fixture "$BATS_TEST_TMPDIR/roles" implement shared
    export FOUNDRY_ROLES_DIR="$BATS_TEST_TMPDIR/roles"
    export WORKER_ROLE="implement"
    # The operator switch (ADR 0110); tests that need the nested daemon set it themselves.
    unset FOUNDRY_NESTED_DOCKER
    export WORKER_CAPABILITY="full"
    unset REVIEW_COMMIT

    # The suite also runs inside a live worker, whose /workspace is the agent's own
    # checkout (and whose image restores any solution found there).  Redirect the
    # entrypoint's clone target to a per-test directory the fake git clone creates.
    export FOUNDRY_WORKSPACE_DIR="$BATS_TEST_TMPDIR/workspace"

    # The entrypoint clones the base bundle the Publisher leaves on the Hand-off volume.
    # Fake-git tests only need it to exist; the bundle tests write a real one.
    export FOUNDRY_HANDOFF_DIR="$BATS_TEST_TMPDIR/handoff"
    /bin/mkdir -p "$FOUNDRY_HANDOFF_DIR/in"
    : > "$FOUNDRY_HANDOFF_DIR/in/base.bundle"

    # Inside a live worker the passwd home holds the rootless dockerd's runtime dir, so
    # answer `getent passwd <uid>` with a per-test home (see make_fake_getent).
    make_fake_getent "$BATS_TEST_TMPDIR/passwd-home"

    # Derive the expected runtime home the same way production does — via the
    # passwd database — so tests remain correct even when the runner sets a
    # custom $HOME that differs from the passwd entry.
    EXPECTED_RUNTIME_HOME="$(getent passwd "$(id -u)" | cut -d: -f6)"

    # Load start_rootless_dockerd directly from the entrypoint — no path redirect
    # needed because XDG_RUNTIME_DIR is a passwd-sourced path, and the fake passwd
    # home is a per-test directory the test user owns.
    load_function
}

# Fake `getent` that answers `passwd <current uid>` with a home of $1 and defers to the
# real binary for every other query, so the passwd-derived runtime dir never resolves
# to the live worker's (whose rootless dockerd keeps its socket and state there).
make_fake_getent() {
    local fake_home="$1"
    local real_getent uid entry
    real_getent="$(command -v getent)"
    uid="$(id -u)"
    entry="$(id -un):x:${uid}:$(id -g)::${fake_home}:/bin/bash"
    /bin/mkdir -p "$fake_home"
    cat > "$FAKE_BIN_DIR/getent" <<EOF
#!/bin/bash
if [[ "\$#" -eq 2 && "\$1" == "passwd" && "\$2" == "${uid}" ]]; then
    printf '%s\\n' '${entry}'
    exit 0
fi
exec "${real_getent}" "\$@"
EOF
    chmod +x "$FAKE_BIN_DIR/getent"
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

# Loads start_rootless_dockerd, and the clear_docker_environment it calls, from
# entrypoint.sh into the current shell.
load_function() {
    local func_name func_body
    for func_name in clear_docker_environment start_rootless_dockerd; do
        func_body="$(awk "/^${func_name}\\(\\)/,/^\\}/" "$ENTRYPOINT")"
        eval "$func_body"
    done
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
    clone)  mkdir -p "$FOUNDRY_WORKSPACE_DIR" ;;
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
    export WORKER_PROMPT="test-prompt"
    export SYSTEM_PROMPT="test-system"
    export ISSUE_NUMBER="1"
    export CLAUDE_SETTINGS_JSON=""

    run bash "$ENTRYPOINT"

    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
}

# A hardcoded `prepare_csharp_lsp /workspace` would pass every other test silently
# (find on a missing path yields no targets), so pin the literal to the one default.
@test "entrypoint names /workspace only as the WORKSPACE_DIR default" {
    local count
    count="$(grep -v '^[[:space:]]*#' "$ENTRYPOINT" | grep -o '/workspace' | wc -l)"

    [ "$count" -eq 1 ]
    grep -qx 'readonly WORKSPACE_DIR="${FOUNDRY_WORKSPACE_DIR:-/workspace}"' "$ENTRYPOINT"
}

# ---------------------------------------------------------------------------
# Shared helpers for bootstrap-failure sentinel tests
# ---------------------------------------------------------------------------

# Minimal env vars needed for the entrypoint to reach the bootstrap steps
set_required_env() {
    export ANTHROPIC_API_KEY="test-key"
    export CLONE_URL="https://github.com/example/repo"
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

# Write a fake git where switch exits non-zero (bootstrap-failure: branch)
make_fake_git_switch_fail() {
    cat > "$FAKE_BIN_DIR/git" <<'EOF'
#!/bin/bash
case "${1:-}" in
    clone)  mkdir -p "$FOUNDRY_WORKSPACE_DIR" ;;
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
    export FOUNDRY_NESTED_DOCKER=1
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
    export FOUNDRY_NESTED_DOCKER=1
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
    export FOUNDRY_NESTED_DOCKER=1
    make_fake_git
    make_fake_claude
    make_fake_dockerd_fail
    make_fake_docker_unhealthy

    run bash "$ENTRYPOINT"

    [[ "$output" == *"FOUNDRY_DOCKER_UNAVAILABLE"* ]]
}

@test "dockerd failure: entrypoint does NOT emit FOUNDRY_BOOTSTRAP_FAILED" {
    set_required_env
    export FOUNDRY_NESTED_DOCKER=1
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

# Write a fake git that handles clone like make_fake_git but passes
# `git config --global` through to the real git, so the identity the entrypoint
# sets lands in the per-test $HOME/.gitconfig and can be read back.
make_fake_git_real_global_config() {
    local real_git
    real_git="$(command -v git)"
    cat > "$FAKE_BIN_DIR/git" <<EOF
#!/bin/bash
if [[ "\${1:-}" == "config" && "\${2:-}" == "--global" ]]; then
    exec "${real_git}" "\$@"
fi
case "\${1:-}" in
    clone) mkdir -p "\$FOUNDRY_WORKSPACE_DIR" ;;
    *)     true ;;
esac
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/git"
}

# Write a fake git where `git config --global` exits non-zero (bootstrap-failure: git-identity)
make_fake_git_global_config_fail() {
    cat > "$FAKE_BIN_DIR/git" <<'EOF'
#!/bin/bash
if [[ "${1:-}" == "config" && "${2:-}" == "--global" ]]; then
    exit 1
fi
case "${1:-}" in
    clone) mkdir -p "$FOUNDRY_WORKSPACE_DIR" ;;
    *)     true ;;
esac
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/git"
}

@test "git identity: bootstrap sets the fixed global user.name and user.email" {
    set_required_env
    make_fake_git_real_global_config
    make_fake_claude
    unset GIT_CONFIG_GLOBAL GH_TOKEN GITLAB_TOKEN

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
    [ "$(git config --global user.name)" = "Foundry Worker" ]
    [ "$(git config --global user.email)" = "foundry-worker@foundry.invalid" ]
}

# Write a fake claude that records GIT_EDITOR as claude sees it ("<unset>" when it is not set).
make_fake_claude_record_git_editor() {
    local git_editor_file="$BATS_TEST_TMPDIR/claude_git_editor"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
printf '%s' "\${GIT_EDITOR-<unset>}" > "${git_editor_file}"
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

# ADR 0101: the agent catches up by rebase, and `git rebase --continue` opens an editor for each commit
# message; a container has no terminal for one, so the rebase would hang until the run times out.
@test "git identity: bootstrap exports GIT_EDITOR=true so git rebase --continue never waits on an editor" {
    set_required_env
    make_fake_git_real_global_config
    make_fake_claude_record_git_editor
    unset GIT_CONFIG_GLOBAL GH_TOKEN GITLAB_TOKEN GIT_EDITOR

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(cat "$BATS_TEST_TMPDIR/claude_git_editor")" = "true" ]
}

@test "git identity: a failing git config emits FOUNDRY_BOOTSTRAP_FAILED stage=git-identity and claude never runs" {
    set_required_env
    make_fake_git_global_config_fail
    make_fake_claude
    unset GH_TOKEN GITLAB_TOKEN

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=git-identity"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

# ---------------------------------------------------------------------------
# Helper: build a PATH that excludes the named binaries (e.g. csharp-ls)
# while retaining all other tools the entrypoint needs. It shadows them from
# the current PATH, which setup_file has already stripped of dockerd-rootless.sh.
make_sanitized_path_without() {
    # Args: names to exclude (e.g. "csharp-ls")
    local sanitized="$PATH" name
    for name in "$@"; do
        sanitized="$(PATH="$sanitized" shadow_from_path "$name")" || return 1
    done
    printf '%s' "$sanitized"
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
    make_fake_claude_record_args_transient_then_success
    make_fixture_tree "$FOUNDRY_WORKSPACE_DIR" src/App.slnx

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
    make_fake_claude_record_args_transient_then_success
    make_fixture_tree "$FOUNDRY_WORKSPACE_DIR" App.sln
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
    make_fake_claude_record_args_transient_then_success
    make_fixture_tree "$FOUNDRY_WORKSPACE_DIR" App.sln
    local safe_path
    safe_path="$(make_sanitized_path_without csharp-ls)"

    run env PATH="$safe_path" bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$output" == *"C# LSP: disabled (csharp-ls not on PATH)"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/dotnet_calls" ]
    ! grep -q -- "--plugin-dir /opt/claude-plugins/csharp-lsp" "$BATS_TEST_TMPDIR/claude_args"
}

@test "csharp lsp entrypoint: the restore runs with no GIT_PAT and no provider CLI, immediately before claude" {
    set_required_env
    make_fake_git
    make_fake_csharp_ls
    make_fake_dotnet
    make_fake_claude_record_args_transient_then_success
    make_fixture_tree "$FOUNDRY_WORKSPACE_DIR" App.sln
    export GH_TOKEN="test-gh-token" GIT_PAT="ghp_leaked"
    local order_file="$BATS_TEST_TMPDIR/order"
    cat > "$FAKE_BIN_DIR/gh" <<EOF
#!/bin/bash
echo gh >> "${order_file}"
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/gh"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(cat "$order_file")" = "$(printf '%s\n' 'dotnet GIT_PAT=unset' claude claude)" ]
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

assert_top_level_combinator_fails_bootstrap() {
    local keyword="$1"
    local schema_file="$FOUNDRY_ROLES_DIR/implement/verdict.schema.json"
    set_required_env
    make_fake_git
    make_fake_claude
    printf '{"type":"object","required":["outcome"],"%s":[{"required":["outcome"]}]}\n' "$keyword" > "$schema_file"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=role"* ]]
    [[ "$output" == *"$schema_file has a top-level $keyword"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
    [ ! -d "$FOUNDRY_WORKSPACE_DIR" ]
}

@test "verdict: a nested allOf under a top-level if/then/else passes to claude unchanged" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args
    local schema='{"type":"object","required":["outcome"],"properties":{"outcome":{"allOf":[{"enum":["done","needs_input"]}]}},"if":{"properties":{"outcome":{"const":"needs_input"}},"required":["outcome"]},"then":{"required":["questions"]},"else":{"required":["pullRequest"]}}'
    printf '%s\n' "$schema" > "$FOUNDRY_ROLES_DIR/implement/verdict.schema.json"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$(head -n 1 "$BATS_TEST_TMPDIR/claude_args")" == *"--json-schema $schema"* ]]
}

@test "verdict: a verdict schema with a top-level allOf fails bootstrap at stage=role before cloning" {
    assert_top_level_combinator_fails_bootstrap allOf
}

@test "verdict: a verdict schema with a top-level anyOf fails bootstrap at stage=role before cloning" {
    assert_top_level_combinator_fails_bootstrap anyOf
}

@test "verdict: a verdict schema with a top-level oneOf fails bootstrap at stage=role before cloning" {
    assert_top_level_combinator_fails_bootstrap oneOf
}

@test "verdict: a verdict schema with several top-level combinators names each one in the bootstrap failure" {
    set_required_env
    make_fake_git
    make_fake_claude
    printf '%s\n' '{"type":"object","required":["outcome"],"allOf":[{"required":["outcome"]}],"oneOf":[{"required":["outcome"]}]}' > "$FOUNDRY_ROLES_DIR/implement/verdict.schema.json"

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=role"* ]]
    [[ "$output" == *"has a top-level allOf, oneOf"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
    [ ! -d "$FOUNDRY_WORKSPACE_DIR" ]
}

# ---------------------------------------------------------------------------
# Capability profile tests (ADR 0098)
#
# The entrypoint chooses the claude flags, the clone depth and the provider
# credentials from WORKER_CAPABILITY, never from WORKER_ROLE.
# ---------------------------------------------------------------------------

# Write a fake claude that records, per invocation, its argv to claude_args and
# whether each provider credential reached it to claude_env.  The first call
# fails transiently and the second (-c resume) succeeds, so both invocations run.
make_fake_claude_record_args_and_env_transient_then_success() {
    local args_file="$BATS_TEST_TMPDIR/claude_args"
    local env_file="$BATS_TEST_TMPDIR/claude_env"
    cat > "$FAKE_BIN_DIR/claude" <<FAKE
#!/bin/bash
printf '%s\n' "\$*" >> "${args_file}"
printf 'GH_TOKEN=%s GITLAB_TOKEN=%s GIT_PAT=%s BRANCH_NAME=%s\n' \
    "\${GH_TOKEN-unset}" "\${GITLAB_TOKEN-unset}" "\${GIT_PAT-unset}" "\${BRANCH_NAME-unset}" >> "${env_file}"
if [ "\$(wc -l < "${args_file}")" -eq 1 ]; then
    printf '%s\n' '{"is_error":true,"api_error_status":503,"result":"API Error","type":"result"}'
    exit 1
fi
printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
exit 0
FAKE
    chmod +x "$FAKE_BIN_DIR/claude"
}

# Write a fake git that records every invocation's argv to git_args.
make_fake_git_record_args() {
    local args_file="$BATS_TEST_TMPDIR/git_args"
    cat > "$FAKE_BIN_DIR/git" <<FAKE
#!/bin/bash
printf '%s\n' "\$*" >> "${args_file}"
case "\${1:-}" in
    clone) mkdir -p "\$FOUNDRY_WORKSPACE_DIR" ;;
esac
exit 0
FAKE
    chmod +x "$FAKE_BIN_DIR/git"
}

# Fake gh/glab that record they were asked to set up push credentials.
make_fake_provider_clis_record_calls() {
    local calls_file="$BATS_TEST_TMPDIR/provider_cli_calls"
    local cli
    for cli in gh glab; do
        cat > "$FAKE_BIN_DIR/$cli" <<FAKE
#!/bin/bash
printf '%s %s\n' "$cli" "\$*" >> "${calls_file}"
exit 0
FAKE
        chmod +x "$FAKE_BIN_DIR/$cli"
    done
}

@test "capability: a restricted run starts both claude invocations with --restricted --strict-mcp-config" {
    set_required_env
    export WORKER_CAPABILITY="restricted"
    make_fake_git
    make_fake_claude_record_args_and_env_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(wc -l < "$BATS_TEST_TMPDIR/claude_args")" -eq 2 ]
    local first resume
    first="$(sed -n 1p "$BATS_TEST_TMPDIR/claude_args")"
    resume="$(sed -n 2p "$BATS_TEST_TMPDIR/claude_args")"
    [[ "$first" =~ (^| )--restricted( |$) ]]
    [[ "$first" =~ (^| )--strict-mcp-config( |$) ]]
    [[ "$resume" =~ (^| )-c( |$) ]]
    [[ "$resume" =~ (^| )--restricted( |$) ]]
    [[ "$resume" =~ (^| )--strict-mcp-config( |$) ]]
}

@test "capability: a restricted run never passes --dangerously-skip-permissions" {
    set_required_env
    export WORKER_CAPABILITY="restricted"
    make_fake_git
    make_fake_claude_record_args_and_env_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(wc -l < "$BATS_TEST_TMPDIR/claude_args")" -eq 2 ]
    ! grep -q -- "--dangerously-skip-permissions" "$BATS_TEST_TMPDIR/claude_args"
}

@test "capability: a restricted run passes the role's verdict schema to both claude invocations" {
    set_required_env
    export WORKER_CAPABILITY="restricted"
    make_fake_git
    make_fake_claude_record_args_and_env_transient_then_success
    printf '%s\n' '{"type":"object","required":["outcome"]}' > "$FOUNDRY_ROLES_DIR/implement/verdict.schema.json"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$(sed -n 1p "$BATS_TEST_TMPDIR/claude_args")" == *'--json-schema {"type":"object","required":["outcome"]}'* ]]
    [[ "$(sed -n 2p "$BATS_TEST_TMPDIR/claude_args")" == *'--json-schema {"type":"object","required":["outcome"]}'* ]]
}

@test "capability: a restricted run reaches claude with no provider token, no GIT_PAT and no branch" {
    set_required_env
    export WORKER_CAPABILITY="restricted"
    export GH_TOKEN="ghp_leaked"
    export GITLAB_TOKEN="glpat-leaked"
    export BRANCH_NAME="feat/1-leaked"
    make_fake_git
    make_fake_provider_clis_record_calls
    make_fake_claude_record_args_and_env_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(sort -u "$BATS_TEST_TMPDIR/claude_env")" = "GH_TOKEN=unset GITLAB_TOKEN=unset GIT_PAT=unset BRANCH_NAME=unset" ]
    [ ! -f "$BATS_TEST_TMPDIR/provider_cli_calls" ]
}

@test "capability: a restricted run clones the base bundle and switches to no branch" {
    set_required_env
    export WORKER_CAPABILITY="restricted"
    export BRANCH_NAME="feat/1-leaked"
    make_fake_git_record_args
    make_fake_claude_record_args

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(grep '^clone ' "$BATS_TEST_TMPDIR/git_args")" = "clone --quiet $FOUNDRY_HANDOFF_DIR/in/base.bundle $FOUNDRY_WORKSPACE_DIR" ]
    ! grep -q '^switch ' "$BATS_TEST_TMPDIR/git_args"
}


# Write a fake dockerd-rootless.sh that records it was started, then fails fast.
make_fake_dockerd_record_start() {
    local started_file="$BATS_TEST_TMPDIR/dockerd_started"
    cat > "$FAKE_BIN_DIR/dockerd-rootless.sh" <<FAKE
#!/bin/bash
echo "dockerd-started" > "${started_file}"
exit 1
FAKE
    chmod +x "$FAKE_BIN_DIR/dockerd-rootless.sh"
}

# Write a fake claude that records the DOCKER_HOST and Testcontainers host it was started with.
make_fake_claude_record_docker_env() {
    local env_file="$BATS_TEST_TMPDIR/claude_docker_env"
    cat > "$FAKE_BIN_DIR/claude" <<FAKE
#!/bin/bash
printf 'DOCKER_HOST=%s TESTCONTAINERS_HOST_OVERRIDE=%s\n' "\${DOCKER_HOST-unset}" "\${TESTCONTAINERS_HOST_OVERRIDE-unset}" > "${env_file}"
exit 0
FAKE
    chmod +x "$FAKE_BIN_DIR/claude"
}

@test "capability: a restricted run starts no dockerd even when dockerd-rootless.sh is on PATH" {
    set_required_env
    export WORKER_CAPABILITY="restricted"
    make_fake_git
    make_fake_claude_record_docker_env
    make_fake_docker_unhealthy
    make_fake_dockerd_record_start

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ ! -f "$BATS_TEST_TMPDIR/dockerd_started" ]
    [[ "$output" != *"Starting rootless dockerd"* ]]
    [[ "$output" != *"FOUNDRY_DOCKER_UNAVAILABLE"* ]]
}

@test "capability: a restricted run reaches claude with DOCKER_HOST and the Testcontainers host unset" {
    set_required_env
    export WORKER_CAPABILITY="restricted"
    export DOCKER_HOST="unix:///leaked.sock"
    export TESTCONTAINERS_HOST_OVERRIDE="leaked"
    make_fake_git
    make_fake_claude_record_docker_env
    make_fake_docker_unhealthy
    make_fake_dockerd_record_start

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(cat "$BATS_TEST_TMPDIR/claude_docker_env")" = "DOCKER_HOST=unset TESTCONTAINERS_HOST_OVERRIDE=unset" ]
}

@test "capability: a full run with nested Docker enabled starts dockerd when dockerd-rootless.sh is on PATH" {
    set_required_env
    export FOUNDRY_NESTED_DOCKER=1
    make_fake_git
    make_fake_claude_record_docker_env
    make_fake_docker_unhealthy
    make_fake_dockerd_record_start

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/dockerd_started" ]
    [[ "$output" == *"Starting rootless dockerd"* ]]
    [[ "$output" == *"FOUNDRY_DOCKER_UNAVAILABLE"* ]]
}

@test "capability: a full run without nested Docker starts no dockerd and prints no unavailable line" {
    set_required_env
    make_fake_git
    make_fake_claude_record_docker_env
    make_fake_docker_unhealthy
    make_fake_dockerd_record_start

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ ! -f "$BATS_TEST_TMPDIR/dockerd_started" ]
    [[ "$output" != *"Starting rootless dockerd"* ]]
    [[ "$output" != *"FOUNDRY_DOCKER_UNAVAILABLE"* ]]
}

@test "capability: a full run whose nested Docker variable is not 1 starts no dockerd" {
    set_required_env
    export FOUNDRY_NESTED_DOCKER=true
    make_fake_git
    make_fake_claude_record_docker_env
    make_fake_docker_unhealthy
    make_fake_dockerd_record_start

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ ! -f "$BATS_TEST_TMPDIR/dockerd_started" ]
}

@test "capability: a full run without nested Docker reaches claude with DOCKER_HOST and the Testcontainers host unset" {
    set_required_env
    export DOCKER_HOST="unix:///leaked.sock"
    export TESTCONTAINERS_HOST_OVERRIDE="leaked"
    make_fake_git
    make_fake_claude_record_docker_env
    make_fake_docker_unhealthy
    make_fake_dockerd_record_start

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(cat "$BATS_TEST_TMPDIR/claude_docker_env")" = "DOCKER_HOST=unset TESTCONTAINERS_HOST_OVERRIDE=unset" ]
}

@test "capability: a restricted run with nested Docker enabled starts no dockerd" {
    set_required_env
    export WORKER_CAPABILITY="restricted"
    export FOUNDRY_NESTED_DOCKER=1
    make_fake_git
    make_fake_claude_record_docker_env
    make_fake_docker_unhealthy
    make_fake_dockerd_record_start

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ ! -f "$BATS_TEST_TMPDIR/dockerd_started" ]
}
@test "capability: a full run keeps --dangerously-skip-permissions and no restricted flags on both invocations" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args_and_env_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(wc -l < "$BATS_TEST_TMPDIR/claude_args")" -eq 2 ]
    [ "$(grep -c -- "--dangerously-skip-permissions" "$BATS_TEST_TMPDIR/claude_args")" -eq 2 ]
    ! grep -q -- "--restricted" "$BATS_TEST_TMPDIR/claude_args"
    ! grep -q -- "--strict-mcp-config" "$BATS_TEST_TMPDIR/claude_args"
}

@test "capability: a full run passes the same flags in the same order as before" {
    set_required_env
    make_fake_git
    make_fake_claude_record_args_and_env_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local plugins="--plugin-dir $FOUNDRY_ROLES_DIR/implement --plugin-dir $FOUNDRY_ROLES_DIR/shared"
    [ "$(sed -n 1p "$BATS_TEST_TMPDIR/claude_args")" = "-p test-prompt --append-system-prompt test-system --dangerously-skip-permissions --output-format stream-json --verbose --max-turns 300 $plugins" ]
    [ "$(sed -n 2p "$BATS_TEST_TMPDIR/claude_args")" = "-c -p test-prompt --dangerously-skip-permissions --output-format stream-json --verbose --max-turns 300 $plugins" ]
}

@test "capability: a full run reaches claude with no provider token and clones the base bundle" {
    set_required_env
    export GH_TOKEN="ghp_leaked" GITLAB_TOKEN="glpat-leaked" GIT_PAT="ghp_leaked"
    export BRANCH_NAME="feat/1-work"
    make_fake_git_record_args
    make_fake_provider_clis_record_calls
    make_fake_claude_record_args_and_env_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(sort -u "$BATS_TEST_TMPDIR/claude_env")" = "GH_TOKEN=unset GITLAB_TOKEN=unset GIT_PAT=unset BRANCH_NAME=feat/1-work" ]
    [ ! -f "$BATS_TEST_TMPDIR/provider_cli_calls" ]
    ! grep -q 'credential' "$BATS_TEST_TMPDIR/git_args"
    [ "$(grep '^clone ' "$BATS_TEST_TMPDIR/git_args")" = "clone --quiet $FOUNDRY_HANDOFF_DIR/in/base.bundle $FOUNDRY_WORKSPACE_DIR" ]
}

@test "capability: the entrypoint sets up no provider credential helper" {
    ! grep -Eq 'gh auth setup-git|glab auth git-credential|credential\.' "$ENTRYPOINT"
}

@test "capability: a missing WORKER_CAPABILITY stops the entrypoint before cloning" {
    set_required_env
    unset WORKER_CAPABILITY
    make_fake_git_record_args
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"WORKER_CAPABILITY is required"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/git_args" ]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "capability: an unknown WORKER_CAPABILITY fails bootstrap at stage=role before cloning" {
    set_required_env
    export WORKER_CAPABILITY="admin"
    make_fake_git_record_args
    make_fake_claude

    run bash "$ENTRYPOINT"

    echo "OUT: $output" >&3
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=role"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/git_args" ]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

# ---------------------------------------------------------------------------
# Base bundle clone tests (ADR 0100)
#
# The worker holds no provider credential: it clones the base bundle the
# Publisher wrote to the Hand-off volume.  These tests run the real git against
# a real bundle; only claude is faked.
# ---------------------------------------------------------------------------

# make_base_bundle [run_branch] — writes $FOUNDRY_HANDOFF_DIR/in/base.bundle the
# way the Publisher does: HEAD and the default branch "main", plus run_branch
# (one commit ahead) when given.
make_base_bundle() {
    local seed="$BATS_TEST_TMPDIR/seed"
    command git init --quiet -b main "$seed"
    command git -C "$seed" -c user.name=t -c user.email=t@t commit --quiet --allow-empty -m base
    local refs=(HEAD refs/heads/main)
    if [[ -n "${1:-}" ]]; then
        command git -C "$seed" switch --quiet -c "$1"
        command git -C "$seed" -c user.name=t -c user.email=t@t commit --quiet --allow-empty -m "work on $1"
        command git -C "$seed" switch --quiet main
        refs+=("refs/heads/$1")
    fi
    command git -C "$seed" bundle create "$FOUNDRY_HANDOFF_DIR/in/base.bundle" "${refs[@]}" > /dev/null 2>&1
}

seed_sha() {
    command git -C "$BATS_TEST_TMPDIR/seed" rev-parse "$1"
}

workspace_git() {
    command git -C "$FOUNDRY_WORKSPACE_DIR" "$@"
}

@test "bundle: the entrypoint clones the base bundle into the workspace" {
    set_required_env
    make_base_bundle
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(workspace_git rev-parse HEAD)" = "$(seed_sha main)" ]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "bundle: origin is the credential-less clone URL" {
    set_required_env
    make_base_bundle
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(workspace_git remote get-url origin)" = "https://github.com/example/repo" ]
    [ "$(workspace_git config --get-all remote.origin.url)" = "https://github.com/example/repo" ]
}

@test "bundle: origin/HEAD points at the default branch, so origin/HEAD merges without a fetch" {
    set_required_env
    make_base_bundle
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(workspace_git symbolic-ref refs/remotes/origin/HEAD)" = "refs/remotes/origin/main" ]
    [ "$(workspace_git rev-parse origin/HEAD)" = "$(seed_sha main)" ]
}

@test "bundle: an existing run branch in the bundle is checked out" {
    set_required_env
    export BRANCH_NAME="feat/1-continue"
    make_base_bundle "feat/1-continue"
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(workspace_git symbolic-ref --short HEAD)" = "feat/1-continue" ]
    [ "$(workspace_git rev-parse HEAD)" = "$(seed_sha feat/1-continue)" ]
}

@test "bundle: a fresh run's branch is created from the default branch" {
    set_required_env
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(workspace_git symbolic-ref --short HEAD)" = "feat/1-fresh" ]
    [ "$(workspace_git rev-parse HEAD)" = "$(seed_sha main)" ]
}

@test "bundle: the entrypoint needs no GIT_PAT and claude never sees one" {
    set_required_env
    unset GIT_PAT
    make_base_bundle
    make_fake_claude_record_args_and_env_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(wc -l < "$BATS_TEST_TMPDIR/claude_env")" -eq 2 ]
    [ "$(grep -c ' GIT_PAT=unset ' "$BATS_TEST_TMPDIR/claude_env")" -eq 2 ]
    # The only mention is the defensive unset.
    [ "$(grep -v '^[[:space:]]*#' "$ENTRYPOINT" | grep 'GIT_PAT')" = "unset GH_TOKEN GITLAB_TOKEN GIT_PAT" ]
}

@test "bundle: a missing base bundle fails bootstrap at stage=clone before claude" {
    set_required_env
    rm -f "$FOUNDRY_HANDOFF_DIR/in/base.bundle"
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=clone"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "bundle: a corrupt base bundle fails bootstrap at stage=clone before claude" {
    set_required_env
    printf 'not a bundle\n' > "$FOUNDRY_HANDOFF_DIR/in/base.bundle"
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=clone"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "entrypoint names /handoff only as the HANDOFF_DIR default" {
    [ "$(grep -v '^[[:space:]]*#' "$ENTRYPOINT" | grep -o '/handoff' | wc -l)" -eq 1 ]
    grep -qx 'readonly HANDOFF_DIR="${FOUNDRY_HANDOFF_DIR:-/handoff}"' "$ENTRYPOINT"
}

# ---------------------------------------------------------------------------
# Run bundle tests (ADR 0100, ADR 0101)
#
# When claude exits — or the container is stopped with SIGTERM — the entrypoint
# commits any uncommitted non-ignored changes and writes out/run.bundle holding
# only the run's commits: HEAD --not origin/<branch> origin/HEAD.  These tests
# run the real git against a real base bundle; claude is a stub that runs the
# commands in $BATS_TEST_TMPDIR/claude_actions in the workspace.
# ---------------------------------------------------------------------------

# make_fake_claude_actions <exit_code> — a claude stub that records its provider
# environment to claude_env, runs the bash script $BATS_TEST_TMPDIR/claude_actions
# (if any) in its cwd, then exits with <exit_code>.
make_fake_claude_actions() {
    local exit_code="${1:-0}"
    cat > "$FAKE_BIN_DIR/claude" <<FAKE
#!/bin/bash
printf 'GH_TOKEN=%s GITLAB_TOKEN=%s GIT_PAT=%s\n' "\${GH_TOKEN-unset}" "\${GITLAB_TOKEN-unset}" "\${GIT_PAT-unset}" >> "$BATS_TEST_TMPDIR/claude_env"
if [ -f "$BATS_TEST_TMPDIR/claude_actions" ]; then
    bash -e "$BATS_TEST_TMPDIR/claude_actions"
fi
printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
exit ${exit_code}
FAKE
    chmod +x "$FAKE_BIN_DIR/claude"
}

# claude_actions <script> — what the stub claude does in the workspace.
claude_actions() {
    printf '%s\n' "$1" > "$BATS_TEST_TMPDIR/claude_actions"
}

run_bundle() {
    printf '%s' "$FOUNDRY_HANDOFF_DIR/out/run.bundle"
}

# bundle_prerequisites — the SHAs the run bundle requires (its "-<sha>" header lines).
bundle_prerequisites() {
    sed -n '/^$/q;p' "$(run_bundle)" | sed -n 's/^-\([0-9a-f]*\).*/\1/p'
}

# bundle_run_commits <exclude>... — the subjects of the commits the bundle carries
# beyond <exclude>, fetched into the seed repository.
bundle_run_commits() {
    command git -C "$BATS_TEST_TMPDIR/seed" fetch --quiet "$(run_bundle)" "HEAD:refs/bundle-tip"
    command git -C "$BATS_TEST_TMPDIR/seed" log --format=%s refs/bundle-tip --not "$@"
}

prepare_run_bundle_test() {
    set_required_env
    unset GH_TOKEN GITLAB_TOKEN GIT_PAT
    /bin/mkdir -p "$FOUNDRY_HANDOFF_DIR/out"
}

@test "run bundle: a fresh run's commits are bundled as HEAD --not origin/HEAD" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_claude_actions 0
    claude_actions 'echo a > a.txt; git add a.txt; git commit -qm "first"; echo b > b.txt; git add b.txt; git commit -qm "second"'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$(run_bundle)" ]
    [ "$(bundle_prerequisites)" = "$(seed_sha main)" ]
    [ "$(bundle_run_commits main)" = "$(printf '%s\n' second first)" ]
    [[ "$output" == *"Run bundle: 2 commit(s)"* ]]
}

@test "run bundle: an existing branch's run bundles only the commits beyond origin/<branch>" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-continue"
    make_base_bundle "feat/1-continue"
    make_fake_claude_actions 0
    claude_actions 'git commit -q --allow-empty -m "continued"'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(bundle_run_commits main feat/1-continue)" = "continued" ]
    bundle_prerequisites | grep -qx "$(seed_sha feat/1-continue)"
    [[ "$output" == *"Run bundle: 1 commit(s)"* ]]
}

@test "run bundle: a default-branch commit the run merged in is not part of the bundle" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-continue"
    make_base_bundle "feat/1-continue"
    # The default branch moves on after the run branch forked, and the base
    # bundle carries both heads.
    local seed="$BATS_TEST_TMPDIR/seed"
    command git -C "$seed" -c user.name=t -c user.email=t@t commit --quiet --allow-empty -m "main moved"
    command git -C "$seed" bundle create "$FOUNDRY_HANDOFF_DIR/in/base.bundle" HEAD refs/heads/main refs/heads/feat/1-continue > /dev/null 2>&1
    make_fake_claude_actions 0
    claude_actions 'git merge -q --no-edit origin/HEAD; git commit -q --allow-empty -m "after merge"'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    # The merged-in default-branch head is a prerequisite, not bundled content.
    bundle_prerequisites | grep -qx "$(seed_sha main)"
    [ "$(bundle_run_commits main feat/1-continue | sed -n 1p)" = "after merge" ]
    [ "$(bundle_run_commits main feat/1-continue | wc -l)" -eq 2 ]
    [[ "$(bundle_run_commits main feat/1-continue | sed -n 2p)" == "Merge "* ]]
}

@test "run bundle: no run commits and a clean tree writes no bundle" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_claude_actions 0

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ ! -e "$(run_bundle)" ]
    [[ "$output" == *"Run bundle: none (no run commits)"* ]]
    [ -z "$(ls -A "$FOUNDRY_HANDOFF_DIR/out")" ]
}

@test "run bundle: the sweep commits tracked and untracked non-ignored changes and leaves ignored files out" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_claude_actions 0
    claude_actions 'printf "*.log\n" > .gitignore; echo one > tracked.txt; git add .gitignore tracked.txt; git commit -qm "work"
echo two >> tracked.txt; echo new > untracked.txt; echo noise > build.log'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(workspace_git log -1 --format=%s)" = "wip: uncommitted changes at run end" ]
    [ "$(workspace_git show --name-only --format= HEAD | sort)" = "$(printf '%s\n' tracked.txt untracked.txt)" ]
    [ -z "$(workspace_git status --porcelain)" ]
    [ -f "$FOUNDRY_WORKSPACE_DIR/build.log" ]
    [ "$(bundle_run_commits main)" = "$(printf '%s\n' "wip: uncommitted changes at run end" work)" ]
    [[ "$output" == *"Run bundle: 2 commit(s)"* ]]
}

@test "run bundle: the sweep alone produces a bundle when the agent never committed" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_claude_actions 0
    claude_actions 'echo new > untracked.txt'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(bundle_run_commits main)" = "wip: uncommitted changes at run end" ]
}

@test "run bundle: the sweep runs no repository hook" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_claude_actions 0
    claude_actions "mkdir -p .git/hooks; printf '#!/bin/sh\nexit 1\n' > .git/hooks/pre-commit; chmod +x .git/hooks/pre-commit; echo new > untracked.txt"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(workspace_git log -1 --format=%s)" = "wip: uncommitted changes at run end" ]
}

@test "run bundle: a clean tree gets no sweep commit" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_claude_actions 0
    claude_actions 'git commit -q --allow-empty -m "only"'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(workspace_git log -1 --format=%s)" = "only" ]
}

@test "run bundle: the bundle is written world-readable and atomically, leaving no temp file" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_claude_actions 0
    claude_actions 'git commit -q --allow-empty -m "only"'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(stat -c '%a' "$(run_bundle)")" = "644" ]
    [ "$(ls -A "$FOUNDRY_HANDOFF_DIR/out")" = "run.bundle" ]
}

@test "run bundle: a stale out/run.bundle is removed when the run has no commits" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_claude_actions 0
    claude_actions "echo planted > '$FOUNDRY_HANDOFF_DIR/out/run.bundle'"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ ! -e "$(run_bundle)" ]
}

@test "run bundle: a failing claude is still swept and bundled and its exit status kept" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_claude_actions 3
    claude_actions 'echo new > untracked.txt'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 3 ]
    [ "$(bundle_run_commits main)" = "wip: uncommitted changes at run end" ]
}

@test "run bundle: the transient-error resume is bundled once, after the resume" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    cat > "$FAKE_BIN_DIR/claude" <<FAKE
#!/bin/bash
echo call >> "$BATS_TEST_TMPDIR/claude_calls"
n="\$(wc -l < "$BATS_TEST_TMPDIR/claude_calls")"
git commit -q --allow-empty -m "call \$n"
if [ "\$n" -eq 1 ]; then
    printf '%s\n' '{"is_error":true,"api_error_status":503,"result":"API Error","type":"result"}'
    exit 1
fi
printf '%s\n' '{"is_error":false,"result":"Completed","subtype":"success","type":"result"}'
exit 0
FAKE
    chmod +x "$FAKE_BIN_DIR/claude"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(bundle_run_commits main)" = "$(printf '%s\n' 'call 2' 'call 1')" ]
    [ "$(grep -c 'Run bundle:' <<< "$output")" -eq 1 ]
}

@test "run bundle: an unwritable out/ is a warning and the run keeps claude's status" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    rm -rf "$FOUNDRY_HANDOFF_DIR/out"
    chmod 0555 "$FOUNDRY_HANDOFF_DIR"
    make_fake_claude_actions 0
    claude_actions 'git commit -q --allow-empty -m "only"'

    run bash "$ENTRYPOINT"
    chmod 0755 "$FOUNDRY_HANDOFF_DIR"

    [ "$status" -eq 0 ]
    [ ! -e "$(run_bundle)" ]
    [[ "$output" == *"WARNING: "*"run bundle"* ]]
}

@test "run bundle: a restricted run sweeps nothing and writes no bundle" {
    prepare_run_bundle_test
    export WORKER_CAPABILITY="restricted"
    export BRANCH_NAME="feat/1-leaked"
    make_base_bundle
    make_fake_claude_actions 0
    claude_actions 'echo new > untracked.txt; git commit -q --allow-empty -m "only"'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ ! -e "$(run_bundle)" ]
    [ "$(workspace_git log -1 --format=%s)" = "only" ]
    [ -n "$(workspace_git status --porcelain)" ]
}

@test "run bundle: claude runs with no provider token and no credential helper configured" {
    prepare_run_bundle_test
    export GH_TOKEN="ghp_leaked" GITLAB_TOKEN="glpat-leaked" GIT_PAT="ghp_leaked"
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_provider_clis_record_calls
    make_fake_claude_actions 0

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(cat "$BATS_TEST_TMPDIR/claude_env")" = "GH_TOKEN=unset GITLAB_TOKEN=unset GIT_PAT=unset" ]
    [ ! -f "$BATS_TEST_TMPDIR/provider_cli_calls" ]
    [ -z "$(workspace_git config --get-regexp '^credential' || true)" ]
    [ -z "$(command git config --global --get-regexp '^credential' || true)" ]
}

# wait_for_file <path> — polls up to ~10 s for <path> to exist.
wait_for_file() {
    local i
    for i in $(seq 1 100); do
        [ -e "$1" ] && return 0
        sleep 0.1
    done
    return 1
}

@test "run bundle: SIGTERM stops claude, then sweeps and bundles before exiting" {
    prepare_run_bundle_test
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    cat > "$FAKE_BIN_DIR/claude" <<FAKE
#!/bin/bash
git commit -q --allow-empty -m "before stop"
echo dirty > untracked.txt
trap 'kill "\$sleeper" 2>/dev/null; echo term > "$BATS_TEST_TMPDIR/claude_got_term"; exit 143' TERM
sleep 30 &
sleeper=\$!
touch "$BATS_TEST_TMPDIR/claude_started"
wait "\$sleeper"
FAKE
    chmod +x "$FAKE_BIN_DIR/claude"

    bash "$ENTRYPOINT" > "$BATS_TEST_TMPDIR/stdout" 2> "$BATS_TEST_TMPDIR/stderr" 3>&- &
    local pid=$!
    wait_for_file "$BATS_TEST_TMPDIR/claude_started"
    kill -TERM "$pid"
    local exit_status=0
    wait "$pid" || exit_status=$?

    [ "$exit_status" -eq 143 ]
    [ -f "$BATS_TEST_TMPDIR/claude_got_term" ]
    [ "$(bundle_run_commits main)" = "$(printf '%s\n' "wip: uncommitted changes at run end" "before stop")" ]
    grep -q "Run bundle: 2 commit(s)" "$BATS_TEST_TMPDIR/stderr"
}

# ---------------------------------------------------------------------------
# Post-result idle stop (#945): once claude has written a result event and then
# nothing on stdout for CLAUDE_POST_RESULT_IDLE_SECONDS, the entrypoint stops it
# and exits with the status the last result reports.  The limit and the stop
# timeout are shortened through their FOUNDRY_* overrides.
# ---------------------------------------------------------------------------

# make_fake_dockerd_absent — a dockerd that never starts, so a run that keeps claude
# alive for seconds leaves no rootless daemon state behind (degraded mode).
make_fake_dockerd_absent() {
    printf '#!/bin/bash\nexit 1\n' > "$FAKE_BIN_DIR/dockerd-rootless.sh"
    chmod +x "$FAKE_BIN_DIR/dockerd-rootless.sh"
}

# make_fake_claude_script <body> — a claude stub running <body>, which may use
# the result_ok / result_error / init_line helpers to write stream lines.
make_fake_claude_script() {
    cat > "$FAKE_BIN_DIR/claude" <<FAKE
#!/bin/bash
result_ok() { printf '%s\n' '{"duration_api_ms":1,"is_error":false,"result":"Done","subtype":"success","type":"result"}'; }
result_error() { printf '%s\n' '{"duration_api_ms":1,"is_error":true,"result":"Failed","subtype":"error_during_execution","type":"result"}'; }
init_line() { printf '%s\n' '{"type":"system","subtype":"init"}'; }
echo "\$\$" > "$BATS_TEST_TMPDIR/claude_pid"
$1
FAKE
    chmod +x "$FAKE_BIN_DIR/claude"
}

prepare_idle_stop_test() {
    set_required_env
    make_fake_git
    make_fake_dockerd_absent
    export FOUNDRY_CLAUDE_POST_RESULT_IDLE_SECONDS=2
    export FOUNDRY_CLAUDE_STOP_TIMEOUT_SECONDS=2
}

@test "idle stop: claude exiting soon after its result keeps claude's own exit status" {
    prepare_idle_stop_test
    make_fake_claude_script 'result_ok; exit 3'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 3 ]
    [[ "$output" != *"claude idle"* ]]
}

@test "idle stop: a successful result followed by silence stops claude and exits 0" {
    prepare_idle_stop_test
    make_fake_claude_script "result_ok; trap 'echo term > \"$BATS_TEST_TMPDIR/claude_got_term\"; exit 143' TERM; sleep 60 & wait"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/claude_got_term" ]
}

@test "idle stop: an error result followed by silence exits 1" {
    prepare_idle_stop_test
    make_fake_claude_script 'result_error; sleep 60'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 1 ]
    [[ "$output" == *"claude idle"* ]]
}

@test "idle stop: claude ignoring TERM is killed after the stop timeout" {
    prepare_idle_stop_test
    make_fake_claude_script "result_ok; trap 'echo term >> \"$BATS_TEST_TMPDIR/claude_got_term\"' TERM; while true; do sleep 0.2; done"

    local started=$SECONDS
    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/claude_got_term" ]
    ! kill -0 "$(cat "$BATS_TEST_TMPDIR/claude_pid")" 2> /dev/null
    (( SECONDS - started < 20 ))
}

@test "idle stop: logs one line naming the limit and the idle time" {
    prepare_idle_stop_test
    make_fake_claude_script 'result_ok; sleep 60'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(grep -c 'claude idle' <<< "$output")" -eq 1 ]
    grep -Eq '^claude idle [0-9]+s after its result \(limit 2s\); stopping it$' <<< "$output"
}

@test "idle stop: a stream line after the result cancels the idle timer" {
    prepare_idle_stop_test
    make_fake_claude_script 'result_ok; sleep 1; printf "%s\n" "{\"type\":\"system\",\"subtype\":\"task_notification\"}"; sleep 4; exit 7'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 7 ]
    [[ "$output" != *"claude idle"* ]]
}

@test "idle stop: only the next result restarts the timer, and the last result sets the status" {
    prepare_idle_stop_test
    make_fake_claude_script 'result_ok; sleep 1; init_line; sleep 3; result_error; sleep 60'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 1 ]
    [ "$(grep -c 'claude idle' <<< "$output")" -eq 1 ]
}

@test "idle stop: the last of several results decides a successful exit" {
    prepare_idle_stop_test
    make_fake_claude_script 'result_error; init_line; result_ok; sleep 60'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$output" == *"claude idle"* ]]
}

@test "idle stop: a run that never writes a result is never stopped for idleness" {
    prepare_idle_stop_test
    make_fake_claude_script 'init_line; sleep 4; exit 5'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 5 ]
    [[ "$output" != *"claude idle"* ]]
}

@test "idle stop: a non-JSON line mentioning a result does not start the timer" {
    prepare_idle_stop_test
    make_fake_claude_script 'printf "%s\n" "not json \"type\":\"result\"" "{\"type\":\"result\""; sleep 4; exit 5'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 5 ]
    [[ "$output" != *"claude idle"* ]]
}

@test "idle stop: a JSON line that only quotes a result event does not start the timer" {
    prepare_idle_stop_test
    make_fake_claude_script 'printf "%s\n" "{\"type\":\"assistant\",\"text\":\"\\\"type\\\":\\\"result\\\"\"}"; sleep 4; exit 5'

    run bash "$ENTRYPOINT"

    [ "$status" -eq 5 ]
    [[ "$output" != *"claude idle"* ]]
}

@test "idle stop: the stop also ends claude's background tasks before the run is finalized" {
    prepare_run_bundle_test
    export FOUNDRY_CLAUDE_POST_RESULT_IDLE_SECONDS=2
    make_fake_dockerd_absent
    export FOUNDRY_CLAUDE_STOP_TIMEOUT_SECONDS=2
    export BRANCH_NAME="feat/1-fresh"
    make_base_bundle
    make_fake_claude_script "git commit -q --allow-empty -m 'agent work'
bash -c 'trap \"\" TERM; echo \$\$ > \"$BATS_TEST_TMPDIR/bg_pid\"; while true; do sleep 0.2; done' > /dev/null 2>&1 &
result_ok
sleep 60"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/bg_pid" ]
    ! kill -0 "$(cat "$BATS_TEST_TMPDIR/bg_pid")" 2> /dev/null
    [ "$(bundle_run_commits main)" = "agent work" ]
}

@test "idle stop: the transient-error resume gets its own idle stop" {
    prepare_idle_stop_test
    cat > "$FAKE_BIN_DIR/claude" <<FAKE
#!/bin/bash
echo call >> "$BATS_TEST_TMPDIR/claude_calls"
if [ "\$(wc -l < "$BATS_TEST_TMPDIR/claude_calls")" -eq 1 ]; then
    printf '%s\n' '{"is_error":true,"api_error_status":503,"result":"API Error","type":"result"}'
    exit 1
fi
printf '%s\n' '{"duration_api_ms":1,"is_error":false,"result":"Done","type":"result"}'
sleep 60
FAKE
    chmod +x "$FAKE_BIN_DIR/claude"

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(wc -l < "$BATS_TEST_TMPDIR/claude_calls")" -eq 2 ]
    [ "$(grep -c 'claude idle' <<< "$output")" -eq 1 ]
}

@test "idle stop: SIGTERM during the idle wait takes the stop path and exits 143" {
    set_required_env
    make_fake_git
    export FOUNDRY_CLAUDE_POST_RESULT_IDLE_SECONDS=60
    make_fake_dockerd_absent
    make_fake_claude_script "result_ok; touch \"$BATS_TEST_TMPDIR/claude_started\"; trap 'exit 143' TERM; sleep 60 & wait"

    bash "$ENTRYPOINT" > "$BATS_TEST_TMPDIR/stdout" 2> "$BATS_TEST_TMPDIR/stderr" 3>&- &
    local pid=$!
    wait_for_file "$BATS_TEST_TMPDIR/claude_started"
    kill -TERM "$pid"
    local exit_status=0
    wait "$pid" || exit_status=$?

    [ "$exit_status" -eq 143 ]
    grep -q "SIGTERM received" "$BATS_TEST_TMPDIR/stderr"
    ! grep -q "claude idle" "$BATS_TEST_TMPDIR/stderr"
}

# ---------------------------------------------------------------------------
# Signal marker (#963): when claude dies from a signal, and the idle stop did not
# end it, the entrypoint writes one FOUNDRY_CLAUDE_SIGNALED line to stderr with
# the container's cgroup pid and memory counters.  The CI runner's cgroup is not
# under the test's control, so the tests check the format, not the values.
# ---------------------------------------------------------------------------

# A counter value: a number, the literal max (no limit), or ? (unreadable).
SIGNAL_MARKER_VALUE='([0-9]+|max|\?)'

# run_entrypoint_split — runs the entrypoint with stdout and stderr in separate
# files, setting ENTRYPOINT_STATUS.
run_entrypoint_split() {
    ENTRYPOINT_STATUS=0
    bash "$ENTRYPOINT" > "$BATS_TEST_TMPDIR/stdout" 2> "$BATS_TEST_TMPDIR/stderr" 3>&- || ENTRYPOINT_STATUS=$?
}

@test "signal marker: claude aborted by SIGABRT writes one marker with every counter to stderr and keeps status 134" {
    prepare_idle_stop_test
    make_fake_claude_script 'kill -ABRT $$'

    run_entrypoint_split

    [ "$ENTRYPOINT_STATUS" -eq 134 ]
    [ "$(grep -c '^FOUNDRY_CLAUDE_SIGNALED ' "$BATS_TEST_TMPDIR/stderr")" -eq 1 ]
    local v="$SIGNAL_MARKER_VALUE"
    grep -Eq "^FOUNDRY_CLAUDE_SIGNALED signal=6 status=134 pids_current=$v pids_peak=$v pids_max=$v pids_refused=$v mem_current=$v mem_peak=$v mem_max=$v oom_kill=$v\$" \
        "$BATS_TEST_TMPDIR/stderr"
    ! grep -q FOUNDRY_CLAUDE_SIGNALED "$BATS_TEST_TMPDIR/stdout"
}

@test "signal marker: claude exiting 1 with its own error writes no marker" {
    prepare_idle_stop_test
    make_fake_claude_script 'result_error; exit 1'

    run_entrypoint_split

    [ "$ENTRYPOINT_STATUS" -eq 1 ]
    ! grep -q FOUNDRY_CLAUDE_SIGNALED "$BATS_TEST_TMPDIR/stderr"
}

@test "signal marker: claude ended by the idle stop writes no marker" {
    prepare_idle_stop_test
    make_fake_claude_script 'result_ok; sleep 60'

    run_entrypoint_split

    [ "$ENTRYPOINT_STATUS" -eq 0 ]
    grep -q "claude idle" "$BATS_TEST_TMPDIR/stderr"
    ! grep -q FOUNDRY_CLAUDE_SIGNALED "$BATS_TEST_TMPDIR/stderr"
}

@test "signal marker: the transient-error resume writes its own marker" {
    prepare_idle_stop_test
    cat > "$FAKE_BIN_DIR/claude" <<FAKE
#!/bin/bash
echo call >> "$BATS_TEST_TMPDIR/claude_calls"
if [ "\$(wc -l < "$BATS_TEST_TMPDIR/claude_calls")" -eq 1 ]; then
    printf '%s\n' '{"is_error":true,"api_error_status":503,"result":"API Error","type":"result"}'
    exit 1
fi
kill -ABRT \$\$
FAKE
    chmod +x "$FAKE_BIN_DIR/claude"

    run_entrypoint_split

    [ "$ENTRYPOINT_STATUS" -eq 134 ]
    [ "$(wc -l < "$BATS_TEST_TMPDIR/claude_calls")" -eq 2 ]
    [ "$(grep -c '^FOUNDRY_CLAUDE_SIGNALED signal=6 status=134 ' "$BATS_TEST_TMPDIR/stderr")" -eq 1 ]
}

# ---------------------------------------------------------------------------
# shadow_from_path
# ---------------------------------------------------------------------------

# Writes an executable script named $2 into directory $1.
make_executable() {
    /bin/mkdir -p "$1"
    printf '#!/bin/sh\nexit 0\n' > "$1/$2"
    chmod +x "$1/$2"
}

@test "shadow_from_path: the named script is hidden while a sibling tool still resolves" {
    local dir="$BATS_TEST_TMPDIR/host-bin"
    make_executable "$dir" dockerd-rootless.sh
    make_executable "$dir" sibling-tool

    local shadowed
    shadowed="$(PATH="$dir:$PATH" shadow_from_path dockerd-rootless.sh)"

    run env PATH="$shadowed" bash -c 'command -v dockerd-rootless.sh'
    [ "$status" -ne 0 ]
    [ -z "$output" ]
    run env PATH="$shadowed" bash -c 'command -v sibling-tool'
    [ "$status" -eq 0 ]
    [ "$(readlink -f "$output")" = "$(readlink -f "$dir/sibling-tool")" ]
}

@test "shadow_from_path: copies in two PATH directories, one a symlink to the other, are both hidden" {
    local dir="$BATS_TEST_TMPDIR/usr-bin"
    make_executable "$dir" dockerd-rootless.sh
    make_executable "$dir" sibling-tool
    ln -s "$dir" "$BATS_TEST_TMPDIR/bin-link"

    local shadowed
    shadowed="$(PATH="$dir:$BATS_TEST_TMPDIR/bin-link:$PATH" shadow_from_path dockerd-rootless.sh)"

    run env PATH="$shadowed" bash -c 'type -a -p dockerd-rootless.sh'
    [ -z "$output" ]
    run env PATH="$shadowed" bash -c 'command -v sibling-tool'
    [ "$status" -eq 0 ]
}

@test "shadow_from_path: a shadowed directory keeps its dotfiles" {
    local dir="$BATS_TEST_TMPDIR/host-bin"
    make_executable "$dir" dockerd-rootless.sh
    make_executable "$dir" .hidden-tool

    local shadowed
    shadowed="$(PATH="$dir:$PATH" shadow_from_path dockerd-rootless.sh)"

    local shadow_dir="${shadowed%%:*}"
    [ "$shadow_dir" != "$dir" ]
    [ -L "$shadow_dir/.hidden-tool" ]
    [ ! -e "$shadow_dir/dockerd-rootless.sh" ]
}

@test "shadow_from_path: directories without the name, empty entries and missing directories are kept in order" {
    local with="$BATS_TEST_TMPDIR/with"
    local without="$BATS_TEST_TMPDIR/without"
    make_executable "$with" dockerd-rootless.sh
    make_executable "$without" other-tool
    local missing="$BATS_TEST_TMPDIR/missing"

    local shadowed
    shadowed="$(PATH="$without::$missing:$with:$without:$PATH" shadow_from_path dockerd-rootless.sh)"

    local entries
    IFS=: read -r -a entries <<< "$shadowed"
    [ "${entries[0]}" = "$without" ]
    [ -z "${entries[1]}" ]
    [ "${entries[2]}" = "$missing" ]
    [ "${entries[3]}" != "$with" ]
    [ ! -e "${entries[3]}/dockerd-rootless.sh" ]
    [ "${entries[4]}" = "$without" ]
}

# ---------------------------------------------------------------------------
# Toolchain report (ADR 0109, #991)
# ---------------------------------------------------------------------------

# make_fake_toolchain <stdout line> <exit code>
# Writes a fake toolchain script that prints <stdout line> and exits with <exit code>.
make_fake_toolchain() {
    cat > "$BATS_TEST_TMPDIR/toolchain.sh" <<FAKE
#!/bin/bash
printf '%s\n' '$1'
exit $2
FAKE
    chmod +x "$BATS_TEST_TMPDIR/toolchain.sh"
}

@test "toolchain: the entrypoint writes the script's FOUNDRY_TOOLCHAIN line to stderr" {
    set_required_env
    make_fake_git
    make_fake_claude

    run --separate-stderr bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$stderr" == *$'\nFOUNDRY_TOOLCHAIN [{"name":"node","version":"22.22.3"}]\n'* ]]
    [[ "$output" != *"FOUNDRY_TOOLCHAIN"* ]]
}

@test "toolchain: the line is written once, before rootless dockerd starts" {
    set_required_env
    export FOUNDRY_NESTED_DOCKER=1
    make_fake_git
    make_fake_claude
    make_fake_docker_unhealthy
    make_fake_dockerd_record_start

    run --separate-stderr bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(grep -c '^FOUNDRY_TOOLCHAIN ' <<< "$stderr")" -eq 1 ]
    local toolchain_line dockerd_line
    toolchain_line="$(grep -n '^FOUNDRY_TOOLCHAIN ' <<< "$stderr" | cut -d: -f1)"
    dockerd_line="$(grep -n 'Starting rootless dockerd' <<< "$stderr" | head -n 1 | cut -d: -f1)"
    [ -n "$dockerd_line" ]
    [ "$toolchain_line" -lt "$dockerd_line" ]
}

@test "toolchain: a restricted run writes the line too" {
    set_required_env
    export WORKER_CAPABILITY="restricted"
    make_fake_git
    make_fake_claude

    run --separate-stderr bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [[ "$stderr" == *'FOUNDRY_TOOLCHAIN [{"name":"node","version":"22.22.3"}]'* ]]
}

@test "toolchain: a failing script is ignored and claude still runs" {
    set_required_env
    make_fake_git
    make_fake_claude
    make_fake_toolchain 'partial' 3

    run --separate-stderr bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
    [[ "$stderr" == *"Toolchain: not reported ($FOUNDRY_TOOLCHAIN_SCRIPT failed)"* ]]
    [[ "$stderr" != *"FOUNDRY_BOOTSTRAP_FAILED"* ]]
}

@test "toolchain: a missing script is ignored and claude still runs" {
    set_required_env
    make_fake_git
    make_fake_claude
    export FOUNDRY_TOOLCHAIN_SCRIPT="$BATS_TEST_TMPDIR/absent-toolchain.sh"

    run --separate-stderr bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
    [[ "$stderr" == *"Toolchain: not reported ($FOUNDRY_TOOLCHAIN_SCRIPT is missing)"* ]]
}

# ---------------------------------------------------------------------------
# Package sources (ADR 0112): a FOUNDRY_NUGET_CONFIG Foundry passes is decoded to
# ~/.foundry/nuget.config and RestoreConfigFile points every restore at it, the C#
# LSP pre-restore included.  An unreadable value never fails the bootstrap.
# ---------------------------------------------------------------------------

# Fake claude that records whether it saw RestoreConfigFile and FOUNDRY_NUGET_CONFIG.
make_fake_claude_record_package_source_env() {
    local env_file="$BATS_TEST_TMPDIR/claude_env"
    local order_file="$BATS_TEST_TMPDIR/order"
    cat > "$FAKE_BIN_DIR/claude" <<EOF
#!/bin/bash
printf 'RestoreConfigFile=%s\n' "\${RestoreConfigFile-unset}" >> "${env_file}"
printf 'FOUNDRY_NUGET_CONFIG=%s\n' "\${FOUNDRY_NUGET_CONFIG-unset}" >> "${env_file}"
echo claude >> "${order_file}"
echo "claude-invoked" > "$BATS_TEST_TMPDIR/claude_called"
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/claude"
}

# Fake dotnet that records the RestoreConfigFile each restore ran with, and the
# content of that file at the time.
make_fake_dotnet_record_restore_config() {
    local order_file="$BATS_TEST_TMPDIR/order"
    local content_file="$BATS_TEST_TMPDIR/restore_config_content"
    cat > "$FAKE_BIN_DIR/dotnet" <<EOF
#!/bin/bash
printf 'dotnet RestoreConfigFile=%s\n' "\${RestoreConfigFile-unset}" >> "${order_file}"
if [[ -n "\${RestoreConfigFile:-}" && -f "\${RestoreConfigFile}" ]]; then
    cat "\${RestoreConfigFile}" > "${content_file}"
fi
exit 0
EOF
    chmod +x "$FAKE_BIN_DIR/dotnet"
}

@test "package sources: without FOUNDRY_NUGET_CONFIG no config is written and RestoreConfigFile stays unset" {
    set_required_env
    unset FOUNDRY_NUGET_CONFIG RestoreConfigFile
    make_fake_git
    make_fake_claude_record_package_source_env

    run --separate-stderr bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ ! -e "$HOME/.foundry/nuget.config" ]
    grep -qx 'RestoreConfigFile=unset' "$BATS_TEST_TMPDIR/claude_env"
    [[ "$stderr" != *"Package sources:"* ]]
}

@test "package sources: a valid FOUNDRY_NUGET_CONFIG is written 0600 and RestoreConfigFile points the LSP restore and claude at it" {
    set_required_env
    make_fake_git
    make_fake_csharp_ls
    make_fake_dotnet_record_restore_config
    make_fake_claude_record_package_source_env
    make_fixture_tree "$FOUNDRY_WORKSPACE_DIR" App.sln
    local config='<configuration><packageSources><add key="group" value="http://host.docker.internal:5287/package-sources/x/index.json" /></packageSources></configuration>'
    FOUNDRY_NUGET_CONFIG="$(printf '%s' "$config" | base64 -w 0)"
    export FOUNDRY_NUGET_CONFIG

    run --separate-stderr bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local file="$HOME/.foundry/nuget.config"
    [ "$(cat "$file")" = "$config" ]
    [ -n "$(find "$file" -perm 0600)" ]
    [ "$(cat "$BATS_TEST_TMPDIR/restore_config_content")" = "$config" ]
    [ "$(cat "$BATS_TEST_TMPDIR/order")" = "$(printf '%s\n' "dotnet RestoreConfigFile=$file" claude)" ]
    grep -qx "RestoreConfigFile=$file" "$BATS_TEST_TMPDIR/claude_env"
    grep -qx 'FOUNDRY_NUGET_CONFIG=unset' "$BATS_TEST_TMPDIR/claude_env"
}

@test "package sources: an undecodable FOUNDRY_NUGET_CONFIG warns and the run continues without the redirect" {
    set_required_env
    unset RestoreConfigFile
    make_fake_git
    make_fake_claude_record_package_source_env
    export FOUNDRY_NUGET_CONFIG='not base64 !!!'

    run --separate-stderr bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
    [[ "$stderr" == *"Package sources: FOUNDRY_NUGET_CONFIG unreadable"* ]]
    [[ "$stderr" != *"FOUNDRY_BOOTSTRAP_FAILED"* ]]
    [ ! -e "$HOME/.foundry/nuget.config" ]
    grep -qx 'RestoreConfigFile=unset' "$BATS_TEST_TMPDIR/claude_env"
    grep -qx 'FOUNDRY_NUGET_CONFIG=unset' "$BATS_TEST_TMPDIR/claude_env"
}

# ---------------------------------------------------------------------------
# Review checkout tests (ADR 0114)
#
# A review run (REVIEW_COMMIT, restricted only) checks the reviewed commit out
# detached from the base bundle's refs/foundry/review and lets claude read the
# review packet in /handoff/in/review, and nothing else on the Hand-off volume.
# ---------------------------------------------------------------------------

# make_review_bundle — writes the base bundle the Publisher writes for a review:
# HEAD and "main", plus refs/foundry/review at a commit on a side branch the bundle
# does not carry.  Exports REVIEW_COMMIT.
make_review_bundle() {
    local seed="$BATS_TEST_TMPDIR/seed"
    command git init --quiet -b main "$seed"
    command git -C "$seed" -c user.name=t -c user.email=t@t commit --quiet --allow-empty -m base
    command git -C "$seed" switch --quiet -c feat/2-reviewed
    command git -C "$seed" -c user.name=t -c user.email=t@t commit --quiet --allow-empty -m reviewed
    command git -C "$seed" switch --quiet main
    command git -C "$seed" update-ref refs/foundry/review feat/2-reviewed
    command git -C "$seed" bundle create "$FOUNDRY_HANDOFF_DIR/in/base.bundle" HEAD refs/heads/main refs/foundry/review > /dev/null 2>&1
    REVIEW_COMMIT="$(seed_sha feat/2-reviewed)"
    export REVIEW_COMMIT
}

prepare_review_test() {
    set_required_env
    export WORKER_CAPABILITY="restricted"
    /bin/mkdir -p "$FOUNDRY_HANDOFF_DIR/out" "$FOUNDRY_HANDOFF_DIR/in/review"
}

@test "review: a restricted run checks the reviewed commit out detached" {
    prepare_review_test
    make_review_bundle
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(workspace_git rev-parse HEAD)" = "$REVIEW_COMMIT" ]
    ! workspace_git symbolic-ref --quiet HEAD
    [ -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "review: the checkout runs git checkout --detach on the reviewed commit after the clone" {
    prepare_review_test
    export REVIEW_COMMIT="0123456789abcdef0123456789abcdef01234567"
    make_fake_git_record_args
    make_fake_claude_record_args

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    local clone_line checkout_line
    clone_line="$(grep -n '^clone ' "$BATS_TEST_TMPDIR/git_args" | cut -d: -f1)"
    checkout_line="$(grep -nx "checkout --quiet --detach $REVIEW_COMMIT" "$BATS_TEST_TMPDIR/git_args" | cut -d: -f1)"
    [ -n "$checkout_line" ]
    [ "$checkout_line" -gt "$clone_line" ]
    ! grep -q '^switch ' "$BATS_TEST_TMPDIR/git_args"
}

@test "review: both claude invocations get --add-dir on the review packet only" {
    prepare_review_test
    export REVIEW_COMMIT="0123456789abcdef0123456789abcdef01234567"
    make_fake_git
    make_fake_claude_record_args_and_env_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(wc -l < "$BATS_TEST_TMPDIR/claude_args")" -eq 2 ]
    [[ "$(sed -n 1p "$BATS_TEST_TMPDIR/claude_args")" == *"--add-dir $FOUNDRY_HANDOFF_DIR/in/review"* ]]
    [[ "$(sed -n 2p "$BATS_TEST_TMPDIR/claude_args")" == *"--add-dir $FOUNDRY_HANDOFF_DIR/in/review"* ]]
    [ "$(grep -o -- '--add-dir [^ ]*' "$BATS_TEST_TMPDIR/claude_args" | sort -u)" = "--add-dir $FOUNDRY_HANDOFF_DIR/in/review" ]
}

@test "review: a review run reaches claude with no branch and writes no run bundle" {
    prepare_review_test
    export BRANCH_NAME="feat/2-leaked"
    make_review_bundle
    make_fake_claude_record_args_and_env_transient_then_success

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    [ "$(sort -u "$BATS_TEST_TMPDIR/claude_env")" = "GH_TOKEN=unset GITLAB_TOKEN=unset GIT_PAT=unset BRANCH_NAME=unset" ]
    [ ! -e "$(run_bundle)" ]
}

@test "review: a run without REVIEW_COMMIT passes no --add-dir and checks out no commit" {
    prepare_review_test
    make_fake_git_record_args
    make_fake_claude_record_args

    run bash "$ENTRYPOINT"

    [ "$status" -eq 0 ]
    ! grep -q -- '--add-dir' "$BATS_TEST_TMPDIR/claude_args"
    ! grep -q '^checkout ' "$BATS_TEST_TMPDIR/git_args"
}

@test "review: REVIEW_COMMIT on a full run fails bootstrap at stage=role before cloning" {
    set_required_env
    export WORKER_CAPABILITY="full"
    export REVIEW_COMMIT="0123456789abcdef0123456789abcdef01234567"
    make_fake_git_record_args
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=role detail=REVIEW_COMMIT is a read-only input and needs WORKER_CAPABILITY=restricted"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/git_args" ]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "review: a REVIEW_COMMIT that is not a 40-hex SHA fails bootstrap at stage=role before cloning" {
    prepare_review_test
    export REVIEW_COMMIT="HEAD~1"
    make_fake_git_record_args
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=role detail=REVIEW_COMMIT is not a 40-hex commit SHA"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/git_args" ]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}

@test "review: a reviewed commit missing from the bundle fails bootstrap at stage=review" {
    prepare_review_test
    make_base_bundle
    export REVIEW_COMMIT="0123456789abcdef0123456789abcdef01234567"
    make_fake_claude

    run bash "$ENTRYPOINT"

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=review"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/claude_called" ]
}
