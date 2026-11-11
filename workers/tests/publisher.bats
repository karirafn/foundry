#!/usr/bin/env bats
# Tests for publisher/publisher.sh (ADR 0100).
#
# The publisher runs real git against a local "remote" repository: a git wrapper first on PATH maps the
# https clone URL the publisher builds to that repository, records each invocation's arguments and the
# environment git saw, and reads the password the GIT_ASKPASS helper would give, before handing over to
# the real git.

PUBLISHER="$(cd "$(dirname "$BATS_TEST_FILENAME")/.." && pwd)/publisher/publisher.sh"

AUTH_URL="https://oauth2@git.example.test/owner/repo.git"

setup() {
    REAL_GIT="$(command -v git)"
    export FAKE_BIN_DIR="$BATS_TEST_TMPDIR/bin"
    mkdir -p "$FAKE_BIN_DIR"
    export PATH="$FAKE_BIN_DIR:$PATH"

    export FOUNDRY_HANDOFF_DIR="$BATS_TEST_TMPDIR/handoff"
    export FOUNDRY_WORK_DIR="$BATS_TEST_TMPDIR/work"
    mkdir -p "$FOUNDRY_HANDOFF_DIR" "$FOUNDRY_WORK_DIR"

    export CLONE_URL="https://git.example.test/owner/repo"
    export GIT_PAT="ghp_publisher_token"
    unset RUN_BRANCH

    REMOTE="$BATS_TEST_TMPDIR/remote.git"
    make_remote main
    make_git_wrapper
}

# make_remote <default_branch> — a bare remote whose default branch has one commit.
make_remote() {
    local seed="$BATS_TEST_TMPDIR/seed"
    "$REAL_GIT" init --quiet -b "$1" "$seed"
    "$REAL_GIT" -C "$seed" -c user.name=t -c user.email=t@t commit --quiet --allow-empty -m base
    "$REAL_GIT" clone --quiet --bare "$seed" "$REMOTE"
}

# add_remote_branch <branch> — a branch one commit ahead of the remote's default branch.
add_remote_branch() {
    local seed="$BATS_TEST_TMPDIR/seed"
    "$REAL_GIT" -C "$seed" switch --quiet -c "$1"
    "$REAL_GIT" -C "$seed" -c user.name=t -c user.email=t@t commit --quiet --allow-empty -m "work on $1"
    "$REAL_GIT" -C "$seed" push --quiet "$REMOTE" "$1"
}

make_git_wrapper() {
    cat > "$FAKE_BIN_DIR/git" <<EOF
#!/bin/bash
printf '%s\n' "\$*" >> "$BATS_TEST_TMPDIR/git_args"
printf 'GIT_CONFIG_NOSYSTEM=%s GIT_CONFIG_GLOBAL=%s\n' "\${GIT_CONFIG_NOSYSTEM-unset}" "\${GIT_CONFIG_GLOBAL-unset}" >> "$BATS_TEST_TMPDIR/git_env"
if [ -n "\${GIT_ASKPASS:-}" ]; then
    "\$GIT_ASKPASS" "Password for '$AUTH_URL': " > "$BATS_TEST_TMPDIR/askpass_answer"
fi
args=()
for arg in "\$@"; do
    if [ "\$arg" = "$AUTH_URL" ]; then
        if [ -n "\${FAKE_GIT_UNREACHABLE:-}" ]; then
            echo "fatal: unable to access '$AUTH_URL': Could not resolve host: git.example.test" >&2
            exit 128
        fi
        arg="$REMOTE"
    fi
    args+=("\$arg")
done
exec "$REAL_GIT" "\${args[@]}"
EOF
    chmod +x "$FAKE_BIN_DIR/git"
}

remote_sha() {
    "$REAL_GIT" -C "$REMOTE" rev-parse "refs/heads/$1"
}

bundle_heads() {
    "$REAL_GIT" bundle list-heads "$FOUNDRY_HANDOFF_DIR/in/base.bundle"
}

@test "prepare writes in/base.bundle holding the default branch as HEAD" {
    run sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    [ -f "$FOUNDRY_HANDOFF_DIR/in/base.bundle" ]
    bundle_heads | grep -qx "$(remote_sha main) refs/heads/main"
    bundle_heads | grep -qx "$(remote_sha main) HEAD"
}

@test "prepare reports the default branch and its head on a single FOUNDRY_BASE line" {
    run sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    [ "$(grep -c '^FOUNDRY_BASE ' <<< "$output")" -eq 1 ]
    grep -qx "FOUNDRY_BASE default_branch=main default_sha=$(remote_sha main)" <<< "$output"
}

@test "prepare follows the remote's default branch, whatever its name" {
    rm -rf "$REMOTE" "$BATS_TEST_TMPDIR/seed"
    make_remote trunk

    run sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    grep -qx "FOUNDRY_BASE default_branch=trunk default_sha=$(remote_sha trunk)" <<< "$output"
}

@test "prepare with RUN_BRANCH also bundles the run's branch and reports its head" {
    add_remote_branch feat/7-work
    export RUN_BRANCH="feat/7-work"

    run sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    bundle_heads | grep -qx "$(remote_sha feat/7-work) refs/heads/feat/7-work"
    bundle_heads | grep -qx "$(remote_sha main) refs/heads/main"
    grep -qx "FOUNDRY_BASE default_branch=main default_sha=$(remote_sha main) branch_sha=$(remote_sha feat/7-work)" <<< "$output"
}

@test "prepare without RUN_BRANCH bundles no other branch" {
    add_remote_branch feat/7-work

    run sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    ! bundle_heads | grep -q "feat/7-work"
}

@test "a worker clone of the bundle checks out the default branch" {
    add_remote_branch feat/7-work
    export RUN_BRANCH="feat/7-work"
    run sh "$PUBLISHER" prepare
    [ "$status" -eq 0 ]

    "$REAL_GIT" clone --quiet "$FOUNDRY_HANDOFF_DIR/in/base.bundle" "$BATS_TEST_TMPDIR/clone"

    [ "$("$REAL_GIT" -C "$BATS_TEST_TMPDIR/clone" rev-parse HEAD)" = "$(remote_sha main)" ]
    [ "$("$REAL_GIT" -C "$BATS_TEST_TMPDIR/clone" rev-parse origin/feat/7-work)" = "$(remote_sha feat/7-work)" ]
}

@test "a missing run branch fails at stage=clone with a branch-missing reason and writes no bundle" {
    export RUN_BRANCH="feat/7-deleted"

    run sh "$PUBLISHER" prepare

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=the run's branch feat/7-deleted does not exist on the remote"* ]]
    [[ "$output" != *"FOUNDRY_BASE"* ]]
    [ ! -e "$FOUNDRY_HANDOFF_DIR/in/base.bundle" ]
}

@test "an empty repository fails at stage=clone with a clear reason" {
    rm -rf "$REMOTE"
    "$REAL_GIT" init --quiet --bare "$REMOTE"

    run sh "$PUBLISHER" prepare

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=the repository has no default branch to clone (is it empty?)"* ]]
    [ ! -e "$FOUNDRY_HANDOFF_DIR/in/base.bundle" ]
}

@test "an unreachable provider fails at stage=clone carrying git's error" {
    export FAKE_GIT_UNREACHABLE=1

    run sh "$PUBLISHER" prepare

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=could not reach the repository: fatal: unable to access"* ]]
}

@test "the token reaches git only through GIT_ASKPASS, never an argument or the output" {
    run sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    [ "$(cat "$BATS_TEST_TMPDIR/askpass_answer")" = "ghp_publisher_token" ]
    ! grep -q "ghp_publisher_token" "$BATS_TEST_TMPDIR/git_args"
    [[ "$output" != *"ghp_publisher_token"* ]]
    grep -q " ls-remote .*$AUTH_URL" "$BATS_TEST_TMPDIR/git_args"
}

@test "the askpass helper is removed when prepare exits" {
    export TMPDIR="$BATS_TEST_TMPDIR/tmp"
    mkdir -p "$TMPDIR"

    run sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    [ -z "$(ls -A "$TMPDIR")" ]
}

@test "git runs with no inherited system or global configuration" {
    printf '[url "https://evil.example.test/"]\n\tinsteadOf = https://\n' > "$BATS_TEST_TMPDIR/gitconfig"
    export GIT_CONFIG_GLOBAL="$BATS_TEST_TMPDIR/gitconfig"

    run sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    [ "$(sort -u "$BATS_TEST_TMPDIR/git_env")" = "GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null" ]
}

@test "the scratch repository is fresh, template-free and has hooks off" {
    mkdir -p "$FOUNDRY_WORK_DIR/repo/hooks"
    printf '#!/bin/sh\ntouch %s\n' "$BATS_TEST_TMPDIR/hook_ran" > "$FOUNDRY_WORK_DIR/repo/hooks/post-checkout"
    chmod +x "$FOUNDRY_WORK_DIR/repo/hooks/post-checkout"

    run sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    [ ! -e "$FOUNDRY_WORK_DIR/repo/hooks" ]
    [ "$("$REAL_GIT" -C "$FOUNDRY_WORK_DIR/repo" config core.hooksPath)" = "/dev/null" ]
    grep -q -- "-c core.hooksPath=/dev/null" "$BATS_TEST_TMPDIR/git_args"
}

@test "an invalid RUN_BRANCH fails before any git transfer" {
    export RUN_BRANCH='feat/7;rm -rf /'

    run sh "$PUBLISHER" prepare

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=invalid RUN_BRANCH characters"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/git_args" ]
}

@test "a missing GIT_PAT stops prepare before any git transfer" {
    unset GIT_PAT

    run sh "$PUBLISHER" prepare

    [ "$status" -ne 0 ]
    [[ "$output" == *"GIT_PAT is required"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/git_args" ]
}

@test "an unknown mode is rejected" {
    run sh "$PUBLISHER" publish-everything

    [ "$status" -eq 64 ]
    [ ! -f "$BATS_TEST_TMPDIR/git_args" ]
}
