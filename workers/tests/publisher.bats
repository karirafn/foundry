#!/usr/bin/env bats
# Tests for publisher/publisher.sh (ADR 0100, ADR 0101).

bats_require_minimum_version 1.5.0
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

    # Publish mode scans with Foundry's own scanner configuration, the file the image copies to /etc/foundry.
    export FOUNDRY_GITLEAKS_CONFIG
    FOUNDRY_GITLEAKS_CONFIG="$(cd "$(dirname "$BATS_TEST_FILENAME")/.." && pwd)/publisher/gitleaks.toml"
    command -v gitleaks > /dev/null || {
        echo "gitleaks must be on PATH to run the publisher suite" >&2
        return 1
    }

    export CLONE_URL="https://git.example.test/owner/repo"
    export GIT_PAT="ghp_publisher_token"
    unset RUN_BRANCH RUN_ID BASE_DEFAULT_SHA BASE_BRANCH_SHA AUTHOR_NAME AUTHOR_EMAIL

    REMOTE="$BATS_TEST_TMPDIR/remote.git"
    make_remote main
    make_git_wrapper
    make_sleep_stub
}

# Git's stderr for each way a transfer can fail, captured from git 2.39.5 on 2026-11-13 with
# GIT_TERMINAL_PROMPT=0 git ls-remote <url> HEAD against the URLs in issue #881's notes (and httpbin.org/status/500,
# 429 and 403 for the other statuses), with the URL, host and port swapped for the test's.
GIT_DNS_FAILURE="fatal: unable to access '$AUTH_URL/': Could not resolve host: git.example.test"
GIT_CONNECTION_REFUSED="fatal: unable to access '$AUTH_URL/': Failed to connect to git.example.test port 443 after 0 ms: Couldn't connect to server"
GIT_HTTP_503="fatal: unable to access '$AUTH_URL/': The requested URL returned error: 503"
GIT_HTTP_500="fatal: unable to access '$AUTH_URL/': The requested URL returned error: 500"
GIT_HTTP_429="fatal: unable to access '$AUTH_URL/': The requested URL returned error: 429"
GIT_HTTP_403="fatal: unable to access '$AUTH_URL/': The requested URL returned error: 403"
GIT_NO_CREDENTIALS="fatal: could not read Username for 'https://git.example.test': terminal prompts disabled"

# provider_errors <stderr>... — the next git transfers with the provider each fail with one <stderr> line, in order;
# transfers after them reach the remote.
provider_errors() {
    printf '%s\n' "$@" >> "$BATS_TEST_TMPDIR/provider_errors"
}

# A sleep that records its argument and returns at once, so retries cost the suite no time.
make_sleep_stub() {
    cat > "$FAKE_BIN_DIR/sleep" <<EOF
#!/bin/sh
printf '%s\n' "\$*" >> "$BATS_TEST_TMPDIR/sleeps"
EOF
    chmod +x "$FAKE_BIN_DIR/sleep"
}

sleeps() {
    cat "$BATS_TEST_TMPDIR/sleeps" 2> /dev/null || true
}

ls_remote_count() {
    grep -c ' ls-remote ' "$BATS_TEST_TMPDIR/git_args" || true
}

# prepare_gives_up_unreachable <stderr> — Prepare meets <stderr> on every attempt, retries it on the 5s, 15s, 40s
# schedule, then fails at stage=unreachable carrying it.
prepare_gives_up_unreachable() {
    provider_errors "$1" "$1" "$1" "$1"

    run sh "$PUBLISHER" prepare

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=unreachable detail=could not reach the repository: $1"* ]]
    [ "$(ls_remote_count)" -eq 4 ]
    [ "$(sleeps)" = "$(printf '5\n15\n40')" ]
    [ ! -e "$FOUNDRY_HANDOFF_DIR/in/base.bundle" ]
}

# prepare_fails_without_retry <stderr> — Prepare fails at stage=clone on its first attempt, carrying <stderr>.
prepare_fails_without_retry() {
    provider_errors "$1"

    run sh "$PUBLISHER" prepare

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=could not reach the repository: $1"* ]]
    [ "$(ls_remote_count)" -eq 1 ]
    [ -z "$(sleeps)" ]
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
        if [ -s "$BATS_TEST_TMPDIR/provider_errors" ]; then
            head -n 1 "$BATS_TEST_TMPDIR/provider_errors" >&2
            sed -i 1d "$BATS_TEST_TMPDIR/provider_errors"
            exit 128
        fi
        if [ -n "\${FAKE_GIT_UNREACHABLE:-}" ]; then
            echo "fatal: unable to access '$AUTH_URL': Could not resolve host: git.example.test" >&2
            exit 128
        fi
        if [ -n "\${FAKE_GIT_AUTH_FAILED:-}" ]; then
            echo "remote: Invalid username or token." >&2
            echo "fatal: Authentication failed for '$AUTH_URL'" >&2
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

@test "prepare creates out/ world-writable so the worker can write its run bundle" {
    run sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    [ -d "$FOUNDRY_HANDOFF_DIR/out" ]
    [ "$(stat -c '%a' "$FOUNDRY_HANDOFF_DIR/out")" = "777" ]
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

@test "an unreachable provider is retried, then fails at stage=unreachable carrying git's last error" {
    export FAKE_GIT_UNREACHABLE=1

    run sh "$PUBLISHER" prepare

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=unreachable detail=could not reach the repository: fatal: unable to access"* ]]
    [ "$(ls_remote_count)" -eq 4 ]
}

@test "a DNS failure is retried after 5s, 15s and 40s, then fails at stage=unreachable" {
    prepare_gives_up_unreachable "$GIT_DNS_FAILURE"
}

@test "a refused connection is retried, then fails at stage=unreachable" {
    prepare_gives_up_unreachable "$GIT_CONNECTION_REFUSED"
}

@test "an HTTP 503 is retried, then fails at stage=unreachable" {
    prepare_gives_up_unreachable "$GIT_HTTP_503"
}

@test "an HTTP 500 is retried, then fails at stage=unreachable" {
    prepare_gives_up_unreachable "$GIT_HTTP_500"
}

@test "an HTTP 429 is retried, then fails at stage=unreachable" {
    prepare_gives_up_unreachable "$GIT_HTTP_429"
}

@test "an HTTP 403 is not retried and fails at stage=clone" {
    prepare_fails_without_retry "$GIT_HTTP_403"
}

@test "git finding no credentials is not retried and fails at stage=clone" {
    prepare_fails_without_retry "$GIT_NO_CREDENTIALS"
}

@test "a rate limit without git's transport prefix is not retried and fails at stage=clone" {
    prepare_fails_without_retry "remote: API rate limit exceeded"
}

@test "an authentication failure is not retried and fails at stage=clone" {
    export FAKE_GIT_AUTH_FAILED=1

    run sh "$PUBLISHER" prepare

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=could not reach the repository: fatal: Authentication failed"* ]]
    [ "$(ls_remote_count)" -eq 1 ]
    [ -z "$(sleeps)" ]
}

@test "each retry prints one stderr line naming its attempt and sleep, and no sentinel" {
    provider_errors "$GIT_DNS_FAILURE" "$GIT_DNS_FAILURE"

    run --separate-stderr sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    [ "$(grep -c 'retrying' <<< "$stderr")" -eq 2 ]
    grep -q 'attempt 1 of 4.*retrying in 5s' <<< "$stderr"
    grep -q 'attempt 2 of 4.*retrying in 15s' <<< "$stderr"
    [[ "$stderr" != *"FOUNDRY_"* ]]
}

@test "a provider reached on a retry prepares the run as if the first attempt had succeeded" {
    add_remote_branch feat/7-work
    export RUN_BRANCH="feat/7-work"
    provider_errors "$GIT_HTTP_503" "$GIT_DNS_FAILURE"

    run --separate-stderr sh "$PUBLISHER" prepare

    [ "$status" -eq 0 ]
    [ "$output" = "FOUNDRY_BASE default_branch=main default_sha=$(remote_sha main) branch_sha=$(remote_sha feat/7-work)" ]
    [[ "$stderr" != *"FOUNDRY_BOOTSTRAP_FAILED"* ]]
    bundle_heads | grep -qx "$(remote_sha feat/7-work) refs/heads/feat/7-work"
    [ "$(sleeps)" = "$(printf '5\n15')" ]
}

@test "a permanent failure on a retry ends the retries at stage=clone" {
    provider_errors "$GIT_DNS_FAILURE" "$GIT_HTTP_403"

    run sh "$PUBLISHER" prepare

    [ "$status" -ne 0 ]
    [[ "$output" == *"FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=could not reach the repository: $GIT_HTTP_403"* ]]
    [[ "$output" != *"stage=unreachable"* ]]
    [ "$(ls_remote_count)" -eq 2 ]
    [ "$(sleeps)" = "5" ]
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

# ---------------------------------------------------------------------------
# Publish mode (ADR 0100, ADR 0101)
#
# A test run: start_run records the run's base the way Prepare does, a worker
# repository cloned from the remote stands in for the worker's /workspace, and
# worker_bundle writes out/run.bundle with the entrypoint's range
# (HEAD --not origin/<branch> origin/HEAD).  Remote-side changes during the run
# are made through the seed repository.
# ---------------------------------------------------------------------------

RUN_BRANCH_NAME="feat/7-work"
ACCOUNT_NAME="Account Owner"
ACCOUNT_EMAIL="owner@example.test"
ROBOT="$(printf '\360\237\244\226')"

# start_run — records the run's base (BASE_DEFAULT_SHA, and BASE_BRANCH_SHA when the
# run branch exists on the remote) and clones the worker repository on the run branch.
start_run() {
    export RUN_BRANCH="$RUN_BRANCH_NAME" RUN_ID="run-42"
    export AUTHOR_NAME="$ACCOUNT_NAME" AUTHOR_EMAIL="$ACCOUNT_EMAIL"
    export BASE_DEFAULT_SHA
    BASE_DEFAULT_SHA="$(remote_sha main)"
    if "$REAL_GIT" -C "$REMOTE" rev-parse --verify --quiet "refs/heads/$RUN_BRANCH" > /dev/null; then
        export BASE_BRANCH_SHA
        BASE_BRANCH_SHA="$(remote_sha "$RUN_BRANCH")"
    else
        export BASE_BRANCH_SHA=""
    fi
    WORKER="$BATS_TEST_TMPDIR/worker"
    "$REAL_GIT" clone --quiet "$REMOTE" "$WORKER"
    wgit switch --quiet "$RUN_BRANCH" 2> /dev/null || wgit switch --quiet -c "$RUN_BRANCH"
}

# wgit <args...> — git in the worker repository as the worker's identity.
wgit() {
    "$REAL_GIT" -C "$WORKER" -c user.name="Foundry Worker" -c user.email=foundry-worker@foundry.invalid "$@"
}

# wcommit <file> <content> <message> — a worker commit writing <content> to <file>.
wcommit() {
    printf '%s\n' "$2" > "$WORKER/$1"
    wgit add "$1"
    printf '%s\n' "$3" | wgit commit --quiet -F -
}

# seed_commit <branch> <file> <content> <message> — a commit pushed to the remote's
# <branch> through the seed repository (a human pushing, or the default branch moving).
seed_commit() {
    local seed="$BATS_TEST_TMPDIR/seed"
    "$REAL_GIT" -C "$seed" fetch --quiet "$REMOTE" "+refs/heads/*:refs/remotes/r/*"
    "$REAL_GIT" -C "$seed" switch --quiet --detach "r/$1"
    printf '%s\n' "$3" > "$seed/$2"
    "$REAL_GIT" -C "$seed" add "$2"
    "$REAL_GIT" -C "$seed" -c user.name=Human -c user.email=human@example.test commit --quiet -m "$4"
    "$REAL_GIT" -C "$seed" push --quiet "$REMOTE" "HEAD:refs/heads/$1"
}

# worker_bundle — writes out/run.bundle the way the entrypoint does.
worker_bundle() {
    mkdir -p "$FOUNDRY_HANDOFF_DIR/out"
    local range=(HEAD --not)
    if wgit rev-parse --verify --quiet "refs/remotes/origin/$RUN_BRANCH" > /dev/null; then
        range+=("refs/remotes/origin/$RUN_BRANCH")
    fi
    range+=(refs/remotes/origin/HEAD)
    wgit bundle create --quiet "$FOUNDRY_HANDOFF_DIR/out/run.bundle" "${range[@]}"
}

publish() {
    run --separate-stderr sh "$PUBLISHER" publish
    # Exactly one result line, and it is the last thing on stdout.
    [ "$(grep -c '^FOUNDRY_PUBLISH ' <<< "$output")" -eq 1 ]
    [[ "${lines[${#lines[@]}-1]}" == "FOUNDRY_PUBLISH "* ]]
}

result_line() {
    printf '%s\n' "${lines[${#lines[@]}-1]}"
}

# message <rev> — the exact commit message of <rev> on the remote.
message() {
    "$REAL_GIT" -C "$REMOTE" cat-file commit "$1" | sed '1,/^$/d'
}

remote_git() {
    "$REAL_GIT" -C "$REMOTE" "$@"
}

push_lines() {
    grep -E '(^| )push( |$)' "$BATS_TEST_TMPDIR/git_args" || true
}

@test "publish: a fresh run is pushed fast-forward to a new branch" {
    start_run
    wcommit a.txt a "first"
    wcommit b.txt b "second"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=published head=$(remote_sha "$RUN_BRANCH") commits=2" ]
    [ "$(remote_git log --format=%s "$RUN_BRANCH" --not main)" = "$(printf '%s\n' second first)" ]
    [ "$(remote_git rev-parse "$RUN_BRANCH~2")" = "$BASE_DEFAULT_SHA" ]
}

@test "publish: an existing branch's run is pushed fast-forward on top of it" {
    add_remote_branch "$RUN_BRANCH_NAME"
    start_run
    wcommit a.txt a "continued"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=published head=$(remote_sha "$RUN_BRANCH") commits=2" ]
    [ "$(remote_git rev-parse "$RUN_BRANCH~1")" = "$BASE_BRANCH_SHA" ]
}

@test "publish: a fast-forward publish never force-pushes and pushes only the run's branch" {
    add_remote_branch "$RUN_BRANCH_NAME"
    start_run
    wcommit a.txt a "continued"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(push_lines | wc -l)" -eq 1 ]
    ! push_lines | grep -qE -- '--force| \+'
    push_lines | grep -qE ":refs/heads/$RUN_BRANCH_NAME\$"
}

@test "publish: each pushed run commit is listed oldest first before the result line" {
    start_run
    wcommit a.txt a "first"
    wcommit b.txt b "second

body line"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    local head
    head="$(remote_sha "$RUN_BRANCH")"
    [ "$(grep '^FOUNDRY_RUN_COMMIT ' <<< "$output")" = "$(printf 'FOUNDRY_RUN_COMMIT %s first\nFOUNDRY_RUN_COMMIT %s second\n' "$(remote_git rev-parse "$head~1")" "$head")" ]
}

@test "publish: no run bundle is nothing to publish" {
    start_run

    publish

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=nothing_to_publish" ]
    [ -z "$(push_lines)" ]
}

@test "publish: a bundle with no commits beyond the base is nothing to publish" {
    add_remote_branch "$RUN_BRANCH_NAME"
    start_run
    mkdir -p "$FOUNDRY_HANDOFF_DIR/out"
    wgit bundle create --quiet "$FOUNDRY_HANDOFF_DIR/out/run.bundle" HEAD --not origin/HEAD

    publish

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=nothing_to_publish" ]
    [ -z "$(push_lines)" ]
    [ "$(remote_sha "$RUN_BRANCH")" = "$BASE_BRANCH_SHA" ]
}

@test "publish: a run tip that does not descend from the run's base fails as invalid" {
    start_run
    wgit switch --quiet --orphan unrelated
    wcommit a.txt a "unrelated history"
    worker_bundle

    publish

    [ "$status" -eq 1 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=failed kind=permanent reason=invalid: run tip does not descend from the run's base" ]
    [ -z "$(push_lines)" ]
}

@test "publish: a corrupt run bundle fails as invalid" {
    start_run
    mkdir -p "$FOUNDRY_HANDOFF_DIR/out"
    printf 'not a bundle\n' > "$FOUNDRY_HANDOFF_DIR/out/run.bundle"

    publish

    [ "$status" -eq 1 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=failed kind=permanent reason=invalid: "* ]]
    [ -z "$(push_lines)" ]
}

@test "publish: a run bundle that is a symlink is never read" {
    start_run
    mkdir -p "$FOUNDRY_HANDOFF_DIR/out"
    ln -s /etc/passwd "$FOUNDRY_HANDOFF_DIR/out/run.bundle"

    publish

    [ "$status" -eq 1 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=failed kind=permanent reason=invalid: "* ]]
}

@test "publish: invalid run inputs fail permanently before any git transfer" {
    start_run
    wcommit a.txt a "first"
    worker_bundle
    local name value
    for name in RUN_ID BASE_DEFAULT_SHA BASE_BRANCH_SHA RUN_BRANCH AUTHOR_EMAIL AUTHOR_NAME; do
        case "$name" in
            RUN_ID) value='run 42;rm' ;;
            BASE_DEFAULT_SHA) value='HEAD~1' ;;
            BASE_BRANCH_SHA) value='not-a-sha' ;;
            RUN_BRANCH) value='feat/7..x' ;;
            AUTHOR_EMAIL) value='owner@example.test> evil' ;;
            AUTHOR_NAME) value='' ;;
        esac
        rm -f "$BATS_TEST_TMPDIR/git_args"
        run --separate-stderr env "$name=$value" sh "$PUBLISHER" publish
        [ "$status" -eq 1 ]
        [[ "${lines[${#lines[@]}-1]}" == "FOUNDRY_PUBLISH result=failed kind=permanent reason=invalid: "* ]]
        [ -z "$(push_lines)" ]
        ! grep -q ' ls-remote ' "$BATS_TEST_TMPDIR/git_args" 2> /dev/null
    done
}

@test "publish: author and committer of every run commit become the account identity, dates kept" {
    start_run
    wcommit a.txt a "first"
    wcommit b.txt b "second"
    worker_bundle
    local original
    original="$(wgit log --format='%at %ct' HEAD --not origin/HEAD)"

    publish

    [ "$status" -eq 0 ]
    [ "$(remote_git log --format='%an <%ae>|%cn <%ce>' "$RUN_BRANCH" --not main | sort -u)" = "$ACCOUNT_NAME <$ACCOUNT_EMAIL>|$ACCOUNT_NAME <$ACCOUNT_EMAIL>" ]
    [ "$(remote_git log --format='%at %ct' "$RUN_BRANCH" --not main)" = "$original" ]
}

@test "publish: a message whose body is only a Co-Authored-By trailer keeps just its subject" {
    start_run
    wcommit a.txt a "Add the widget

Co-Authored-By: Claude <noreply@anthropic.com>"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(message "$RUN_BRANCH")" = "Add the widget" ]
}

@test "publish: every Co-Authored-By trailer is stripped, whoever it names and however it is cased" {
    start_run
    wcommit a.txt a "Fix the parser

Handles empty input.

Co-authored-by: Jane Human <jane@example.test>
co-authored-by: claude <noreply@anthropic.com>
CO-AUTHORED-BY: Someone <s@example.test>
Reviewed-by: Kept Reviewer <kept@example.test>"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(message "$RUN_BRANCH")" = "$(printf 'Fix the parser\n\nHandles empty input.\n\nReviewed-by: Kept Reviewer <kept@example.test>')" ]
}

@test "publish: the Claude Code footer is stripped with and without the robot emoji" {
    start_run
    wcommit a.txt a "With emoji

Body text.

$ROBOT Generated with [Claude Code](https://claude.com/claude-code)

Co-Authored-By: Claude <noreply@anthropic.com>"
    wcommit b.txt b "Without emoji

Generated with [Claude Code](https://claude.ai/code)"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(message "$RUN_BRANCH~1")" = "$(printf 'With emoji\n\nBody text.')" ]
    [ "$(message "$RUN_BRANCH")" = "Without emoji" ]
}

@test "publish: lines that only mention Claude Code or co-authors are kept" {
    start_run
    local text="Document the attribution rules

The Generated with [Claude Code](https://claude.com/claude-code) footer is stripped.
A Co-Authored-By: trailer is stripped too.
Run claude -p with Generated with flags."
    wcommit a.txt a "$text"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(message "$RUN_BRANCH")" = "$text" ]
}

@test "publish: a commit that is only attribution keeps its subject" {
    start_run
    wcommit a.txt a "Co-Authored-By: Claude <noreply@anthropic.com>"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(message "$RUN_BRANCH")" = "Co-Authored-By: Claude <noreply@anthropic.com>" ]
}

@test "publish: a merge commit inside the run commits is rewritten and stays a merge" {
    add_remote_branch "$RUN_BRANCH_NAME"
    start_run
    wgit switch --quiet -c topic
    wcommit topic.txt t "topic work"
    wgit switch --quiet "$RUN_BRANCH"
    wcommit main-line.txt m "branch work"
    wgit merge --quiet --no-ff --no-edit -m "Merge topic

Co-Authored-By: Claude <noreply@anthropic.com>" topic
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    local head
    head="$(remote_sha "$RUN_BRANCH")"
    [ "$(remote_git rev-list --parents -n 1 "$head" | wc -w)" -eq 3 ]
    [ "$(message "$head")" = "Merge topic" ]
    [ "$(remote_git log --format='%an|%cn' "$head" --not main "$BASE_BRANCH_SHA" | sort -u)" = "$ACCOUNT_NAME|$ACCOUNT_NAME" ]
    [ "$(remote_git log --format=%s "$head^2" -1)" = "topic work" ]
    [ "$(remote_git log --format=%s "$head^1" -1)" = "branch work" ]
    [ "$(remote_git rev-parse "$head^1~1")" = "$BASE_BRANCH_SHA" ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=published head=$head commits=4" ]
}

@test "publish: after the run merges the default branch every default-branch commit keeps its SHA" {
    add_remote_branch "$RUN_BRANCH_NAME"
    seed_commit main main1.txt one "main moved once"
    seed_commit main main2.txt two "main moved twice"
    start_run
    wgit merge --quiet --no-edit origin/HEAD
    wcommit a.txt a "after merge"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    local head
    head="$(remote_sha "$RUN_BRANCH")"
    # The default branch's commits are untouched: its head is still an ancestor, by SHA.
    remote_git merge-base --is-ancestor "$BASE_DEFAULT_SHA" "$head"
    [ "$(remote_git rev-parse "$head~1^2")" = "$BASE_DEFAULT_SHA" ]
    [ "$(remote_git rev-parse "$head~1^1")" = "$BASE_BRANCH_SHA" ]
    # Only the run's own commits were rewritten: nothing on the branch beyond main
    # duplicates a default-branch commit.
    [ "$(remote_git log --format=%s "$head" --not main | sort)" = "$(printf '%s\n' "after merge" "Merge remote-tracking branch 'origin/HEAD' into $RUN_BRANCH_NAME" "work on $RUN_BRANCH_NAME" | sort)" ]
    [ "$(grep -c '^FOUNDRY_RUN_COMMIT ' <<< "$output")" -eq 2 ]
}

@test "publish: a remote branch that moved during the run is merged cleanly and the merge pushed" {
    add_remote_branch "$RUN_BRANCH_NAME"
    start_run
    wcommit run.txt run "run work"
    worker_bundle
    seed_commit "$RUN_BRANCH_NAME" human.txt human "human work"
    local human
    human="$(remote_sha "$RUN_BRANCH")"

    publish

    [ "$status" -eq 0 ]
    local head
    head="$(remote_sha "$RUN_BRANCH")"
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=published head=$head commits=4" ]
    [ "$(remote_git rev-list --parents -n 1 "$head" | wc -w)" -eq 3 ]
    [ "$(remote_git rev-parse "$head^2")" = "$human" ]
    [ "$(remote_git log --format=%s -1 "$head^1")" = "run work" ]
    [ "$(remote_git log --format='%an <%ae>|%cn <%ce>' -1 "$head")" = "$ACCOUNT_NAME <$ACCOUNT_EMAIL>|$ACCOUNT_NAME <$ACCOUNT_EMAIL>" ]
    # The merge is Foundry's, not a run commit.
    [ "$(grep '^FOUNDRY_RUN_COMMIT ' <<< "$output")" = "FOUNDRY_RUN_COMMIT $(remote_git rev-parse "$head^1") run work" ]
    ! push_lines | grep -qE -- '--force| \+'
}

@test "publish: a remote branch that moved with a conflicting change diverges to a side branch" {
    add_remote_branch "$RUN_BRANCH_NAME"
    start_run
    wcommit shared.txt "run version" "run work"
    worker_bundle
    seed_commit "$RUN_BRANCH_NAME" shared.txt "human version" "human work"
    local human
    human="$(remote_sha "$RUN_BRANCH")"

    publish

    [ "$status" -eq 0 ]
    local side="$RUN_BRANCH_NAME-run-run-42"
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=diverged branch=$RUN_BRANCH_NAME side_branch=$side head=$(remote_sha "$side")" ]
    [ "$(remote_sha "$RUN_BRANCH")" = "$human" ]
    [ "$(remote_git log --format=%s -1 "$side")" = "run work" ]
    [ "$(remote_git rev-parse "$side~1")" = "$BASE_BRANCH_SHA" ]
    ! push_lines | grep -qE -- '--force| \+'
}

# rebase_run — the default branch moves before Prepare, and the worker rebases the
# run branch onto it (ADR 0101), then commits once more.
rebase_run() {
    add_remote_branch "$RUN_BRANCH_NAME"
    seed_commit main main1.txt one "main moved"
    start_run
    wgit rebase --quiet origin/HEAD
    wcommit a.txt a "after rebase"
    worker_bundle
}

@test "publish: a rebased run tip is pushed with a lease on the run's base" {
    rebase_run

    publish

    [ "$status" -eq 0 ]
    local head
    head="$(remote_sha "$RUN_BRANCH")"
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=published head=$head commits=2" ]
    [ "$(remote_git rev-parse "$head~2")" = "$BASE_DEFAULT_SHA" ]
    [ "$(remote_git log --format=%s "$head" --not main)" = "$(printf '%s\n' "after rebase" "work on $RUN_BRANCH_NAME")" ]
    ! remote_git merge-base --is-ancestor "$BASE_BRANCH_SHA" "$head"
    [ "$(push_lines | wc -l)" -eq 1 ]
    push_lines | grep -qF -- "--force-with-lease=refs/heads/$RUN_BRANCH_NAME:$BASE_BRANCH_SHA"
    push_lines | grep -qE ":refs/heads/$RUN_BRANCH_NAME\$"
}

@test "publish: a rebased run tip whose remote branch moved diverges to a side branch" {
    rebase_run
    seed_commit "$RUN_BRANCH_NAME" human.txt human "human work"
    local human
    human="$(remote_sha "$RUN_BRANCH")"

    publish

    [ "$status" -eq 0 ]
    local side="$RUN_BRANCH_NAME-run-run-42"
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=diverged branch=$RUN_BRANCH_NAME side_branch=$side head=$(remote_sha "$side")" ]
    [ "$(remote_sha "$RUN_BRANCH")" = "$human" ]
    ! push_lines | grep -qE -- '--force| \+'
}

@test "publish: a provider that refuses the lease push diverges with reason force-push-protected" {
    rebase_run
    cat > "$REMOTE/hooks/pre-receive" <<'HOOK'
#!/bin/sh
while read -r old new ref; do
    if [ "$old" != 0000000000000000000000000000000000000000 ] && ! git merge-base --is-ancestor "$old" "$new"; then
        echo "GitLab: You are not allowed to force push code to a protected branch on this project." >&2
        exit 1
    fi
done
HOOK
    chmod +x "$REMOTE/hooks/pre-receive"

    publish

    [ "$status" -eq 0 ]
    local side="$RUN_BRANCH_NAME-run-run-42"
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=diverged branch=$RUN_BRANCH_NAME side_branch=$side head=$(remote_sha "$side") reason=force-push-protected" ]
    [ "$(remote_sha "$RUN_BRANCH")" = "$BASE_BRANCH_SHA" ]
}

@test "publish: a run tip on an existing branch that descends from neither its base nor the default branch fails as invalid" {
    add_remote_branch "$RUN_BRANCH_NAME"
    seed_commit main main1.txt one "main moved"
    start_run
    wgit switch --quiet --orphan unrelated
    wcommit a.txt a "unrelated history"
    worker_bundle

    publish

    [ "$status" -eq 1 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=failed kind=permanent reason=invalid: run tip does not descend from the run's base" ]
    [ -z "$(push_lines)" ]
    [ "$(remote_sha "$RUN_BRANCH")" = "$BASE_BRANCH_SHA" ]
}

# Every conflict resolved by taking the default branch: the rebase drops every branch commit, so the
# tip is the default branch itself and the bundle holds only default-branch commits.
@test "publish: a rebase that drops every run commit is nothing to publish and leaves the branch alone" {
    add_remote_branch "$RUN_BRANCH_NAME"
    seed_commit main main1.txt one "main moved"
    start_run
    wgit rebase --quiet --onto origin/HEAD HEAD
    mkdir -p "$FOUNDRY_HANDOFF_DIR/out"
    wgit bundle create --quiet "$FOUNDRY_HANDOFF_DIR/out/run.bundle" HEAD --not "origin/$RUN_BRANCH"

    publish

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=nothing_to_publish" ]
    [ -z "$(push_lines)" ]
    [ "$(remote_sha "$RUN_BRANCH")" = "$BASE_BRANCH_SHA" ]
}

@test "publish: publishing the same run bundle again is idempotent" {
    start_run
    wcommit a.txt a "first"
    worker_bundle
    publish
    [ "$status" -eq 0 ]
    local first
    first="$(result_line)"

    publish

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "$first" ]
}

@test "publish: an unreachable provider is an unreachable failure that never shows the token" {
    start_run
    wcommit a.txt a "first"
    worker_bundle
    export FAKE_GIT_UNREACHABLE=1

    publish

    [ "$status" -eq 1 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=failed kind=unreachable reason="*"Could not resolve host"* ]]
    [[ "$output" != *"ghp_publisher_token"* ]]
    [[ "$stderr" != *"ghp_publisher_token"* ]]
}

@test "publish: an authentication failure is a permanent failure" {
    start_run
    wcommit a.txt a "first"
    worker_bundle
    export FAKE_GIT_AUTH_FAILED=1

    publish

    [ "$status" -eq 1 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=failed kind=permanent reason="*"Authentication failed"* ]]
}

@test "publish: an unreachable provider fails unreachable at once, without retrying in the script" {
    start_run
    wcommit a.txt a "first"
    worker_bundle
    provider_errors "$GIT_HTTP_503"

    publish

    [ "$status" -eq 1 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=failed kind=unreachable reason=could not reach the repository: $GIT_HTTP_503" ]
    [ -z "$(sleeps)" ]
}

@test "publish: an HTTP 429 is an unreachable failure" {
    start_run
    wcommit a.txt a "first"
    worker_bundle
    provider_errors "$GIT_HTTP_429"

    publish

    [ "$status" -eq 1 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=failed kind=unreachable reason="*"returned error: 429" ]]
}

@test "publish: an HTTP 403 behind git's transport prefix is a permanent failure" {
    start_run
    wcommit a.txt a "first"
    worker_bundle
    provider_errors "$GIT_HTTP_403"

    publish

    [ "$status" -eq 1 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=failed kind=permanent reason="*"returned error: 403" ]]
}

@test "publish: a rate limit without git's transport prefix is a transient failure" {
    start_run
    wcommit a.txt a "first"
    worker_bundle
    provider_errors "remote: API rate limit exceeded"

    publish

    [ "$status" -eq 1 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=failed kind=transient reason="*"rate limit exceeded" ]]
}

@test "publish: the token reaches git only through GIT_ASKPASS and the scratch repository has hooks off" {
    start_run
    wcommit a.txt a "first"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(cat "$BATS_TEST_TMPDIR/askpass_answer")" = "ghp_publisher_token" ]
    ! grep -q "ghp_publisher_token" "$BATS_TEST_TMPDIR/git_args"
    [ "$("$REAL_GIT" -C "$FOUNDRY_WORK_DIR/repo" config core.hooksPath)" = "/dev/null" ]
    [ "$(sort -u "$BATS_TEST_TMPDIR/git_env")" = "GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null" ]
}

# ---------------------------------------------------------------------------
# Diff gate (#788): a detected secret blocks the push; oversized and binary
# files are reverted by one code-authored commit.
# ---------------------------------------------------------------------------

# Values the scanner's default rules detect. Built at run time so this file
# itself holds no literal a scanner would flag.
GITHUB_PAT="ghp_$(printf '%s' 8zN2kQw7RtY5uXc3VbM1aSd9FgH4jKl6PoIq)"
AWS_KEY_ID="AKIA$(printf '%s' 5ZQ7XKD3MNP2RT4W)"
DIFF_GATE_MAX_FILE_BYTES=5242880

# wcommit_bytes <file> <message> — a worker commit of <file> as already written.
wcommit_bytes() {
    wgit add -- "$1"
    wgit commit --quiet -m "$2"
}

# write_binary <path> — a small file git detects as binary (it holds a NUL byte).
write_binary() {
    mkdir -p "$(dirname "$WORKER/$1")"
    printf 'PNG\000\001\002binary %s' "${2:-}" > "$WORKER/$1"
}

# write_oversized <path> — a text file one byte over the size limit.
write_oversized() {
    mkdir -p "$(dirname "$WORKER/$1")"
    head -c "$((DIFF_GATE_MAX_FILE_BYTES + 1))" /dev/zero | tr '\0' 'a' > "$WORKER/$1"
}

secret_findings() {
    grep '^FOUNDRY_SECRET_FINDING ' <<< "$output" || true
}

reverted_files() {
    grep '^FOUNDRY_REVERTED_FILE ' <<< "$output" || true
}

@test "publish: a run commit adding a secret pushes nothing and reports the finding without its value" {
    start_run
    mkdir -p "$WORKER/config"
    wcommit config/app.env "$(printf 'NAME=demo\nTOKEN=%s' "$GITHUB_PAT")" "add config"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=1" ]
    [[ "$(secret_findings)" =~ ^FOUNDRY_SECRET_FINDING\ commit=[0-9a-f]{40}\ line=2\ rule=github-pat\ path=config/app.env$ ]]
    [ -z "$(push_lines)" ]
    ! remote_git rev-parse --verify --quiet "refs/heads/$RUN_BRANCH" > /dev/null
    [[ "$output" != *"$GITHUB_PAT"* ]]
    [[ "$stderr" != *"$GITHUB_PAT"* ]]
    [ -z "$(grep '^FOUNDRY_RUN_COMMIT ' <<< "$output" || true)" ]
}

@test "publish: a secret finding names the run commit as rewritten, never the worker's commit" {
    start_run
    wcommit app.env "TOKEN=$GITHUB_PAT" "add config"
    local worker_sha
    worker_sha="$(wgit rev-parse HEAD)"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    local commit
    commit="$(secret_findings | sed -n 's/.* commit=\([0-9a-f]*\) .*/\1/p')"
    [ -n "$commit" ]
    [ "$commit" != "$worker_sha" ]
    [ "$("$REAL_GIT" -C "$FOUNDRY_WORK_DIR/repo" show -s --format=%an "$commit")" = "$ACCOUNT_NAME" ]
}

@test "publish: every secret in the run commits is reported, one line each" {
    start_run
    wcommit a.env "TOKEN=$GITHUB_PAT" "first"
    wcommit b.env "KEY=$AWS_KEY_ID" "second"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=2" ]
    secret_findings | grep -qE ' rule=github-pat path=a\.env$'
    secret_findings | grep -qE ' rule=aws-access-token path=b\.env$'
}

@test "publish: a secret added in one run commit and removed in a later one still blocks the push" {
    start_run
    wcommit app.env "TOKEN=$GITHUB_PAT" "add secret"
    wcommit app.env "TOKEN=from-the-environment" "remove secret"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=1" ]
    [ -z "$(push_lines)" ]
}

@test "publish: a repository's scanner config, ignore file and inline allow comments are not honoured" {
    start_run
    printf '%s\n' '[allowlist]' 'paths = [".*"]' > "$WORKER/.gitleaks.toml"
    wcommit allowed.env "TOKEN=$GITHUB_PAT # gitleaks:allow" "allowed inline"
    local fingerprint
    fingerprint="$(wgit rev-parse HEAD):allowed.env:github-pat:1"
    printf '%s\n' "$fingerprint" "allowed.env:github-pat:1" > "$WORKER/.gitleaksignore"
    wgit add .gitleaks.toml .gitleaksignore
    wgit commit --quiet -m "scanner configuration"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=1" ]
    secret_findings | grep -qE ' rule=github-pat path=allowed\.env$'
}

@test "publish: a secret on the default branch that the run merged in is not scanned" {
    add_remote_branch "$RUN_BRANCH_NAME"
    seed_commit main legacy.env "TOKEN=$GITHUB_PAT" "legacy secret on main"
    start_run
    wgit merge --quiet --no-edit origin/HEAD
    wcommit a.txt a "run work"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=published "* ]]
    [ -z "$(secret_findings)" ]
}

# Fixture values for the generic-api-key allowlists (#901), built at run time like the values above.
FIXTURE_UUID="$(printf '%s-%s-%s-%s-%s' 3f2b8c1e 9d4a 4e7b a1c6 5f8e2d9b0a47)"
RANDOM_KEY="$(printf '%s' Xq7vR2mK9pLw4ZtN8bYc3HsJ6dFg1QeA)"
# gitleaks' stopwords already pass a name built from common words (app.recent-items.v1), so this stopword-free
# name is the one that proves the versioned-name allowlist: gitleaks 8.30.1's default rules flag it as generic-api-key.
VERSIONED_NAME="qz.wkfjhb-pyx.v1"
DOTTED_VERSIONED_KEY="$(printf '%s.%s.%s.%s' rk live fq2lmzx81pwq v1)"
DOTTED_RANDOM_KEY="$(printf '%s.%s.%s' rk live 9fq2lmzx81pwq7rtt0vb3ncye5)"

# publish_one <path> <line> — a run whose one commit writes <line> to <path>, published.
publish_one() {
    start_run
    mkdir -p "$(dirname "$WORKER/$1")"
    wcommit "$1" "$2" "add $1"
    worker_bundle
    publish
}

assert_published() {
    [ "$status" -eq 0 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=published "* ]]
    [ -z "$(secret_findings)" ]
}

# assert_blocked <rule> <path-regex> — the publish is blocked by exactly one finding, of <rule> in <path-regex>.
assert_blocked() {
    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=1" ]
    secret_findings | grep -qE " rule=$1 path=$2\$"
    [ -z "$(push_lines)" ]
}

@test "publish: a versioned storage-key name is not a secret" {
    publish_one src/store.ts "export const RECENT_KEY = 'app.recent-items.v1';"

    assert_published
}

@test "publish: a versioned name the default rules flag is not a secret" {
    publish_one src/store.ts "export const RECENT_KEY = '$VERSIONED_NAME';"

    assert_published
}

@test "publish: a UUID fixture in a spec file is not a secret" {
    publish_one src/mix.service.spec.ts "const key = '$FIXTURE_UUID';"

    assert_published
}

@test "publish: a UUID fixture in a .NET test file under a tests directory is not a secret" {
    publish_one tests/Api.Tests/IdempotencyTests.cs "var idempotencyKey = \"$FIXTURE_UUID\";"

    assert_published
}

@test "publish: a UUID fixture in a .NET test project directory is not a secret" {
    publish_one src/Foundry.UnitTests/Fixtures.cs "var idempotencyKey = \"$FIXTURE_UUID\";"

    assert_published
}

@test "publish: an uppercase UUID fixture in a test file is not a secret" {
    publish_one src/mix.service.spec.ts "const key = '$(tr 'a-f' 'A-F' <<< "$FIXTURE_UUID")';"

    assert_published
}

@test "publish: a UUID assigned to an api key outside a test file blocks the push" {
    publish_one src/store.ts "const apiKey = '$FIXTURE_UUID';"

    assert_blocked generic-api-key 'src/store\.ts'
}

@test "publish: a UUID in a directory whose name merely ends in test blocks the push" {
    publish_one src/Contest/Scores.cs "var apiKey = \"$FIXTURE_UUID\";"

    assert_blocked generic-api-key 'src/Contest/Scores\.cs'
}

@test "publish: a UUID in a file whose name merely ends in test blocks the push" {
    publish_one web/latest.ts "const apiKey = '$FIXTURE_UUID';"

    assert_blocked generic-api-key 'web/latest\.ts'
}

@test "publish: a random key in a spec file blocks the push" {
    publish_one src/mix.service.spec.ts "const apiKey = '$RANDOM_KEY';"

    assert_blocked generic-api-key 'src/mix\.service\.spec\.ts'
}

@test "publish: a versioned name with digits inside a segment blocks the push" {
    publish_one src/store.ts "const SESSION_KEY = '$DOTTED_VERSIONED_KEY';"

    assert_blocked generic-api-key 'src/store\.ts'
}

@test "publish: a version suffix beyond three digits blocks the push" {
    publish_one src/store.ts "const RECENT_KEY = '${VERSIONED_NAME}234';"

    assert_blocked generic-api-key 'src/store\.ts'
}

@test "publish: a dotted value with a random segment blocks the push" {
    publish_one src/store.ts "const STRIPE_SECRET = '$DOTTED_RANDOM_KEY';"

    assert_blocked generic-api-key 'src/store\.ts'
}

@test "publish: a provider rule still flags a UUID in a test file" {
    publish_one tests/x.spec.ts "const HEROKU_API_KEY = '$FIXTURE_UUID';"

    assert_blocked heroku-api-key 'tests/x\.spec\.ts'
}

@test "publish: a scanner that fails is a failed publish, never a clean scan" {
    start_run
    wcommit a.txt a "first"
    worker_bundle
    cat > "$FAKE_BIN_DIR/gitleaks" <<'SCANNER'
#!/bin/sh
echo "ERR [git] fatal: something went wrong" >&2
exit 0
SCANNER
    chmod +x "$FAKE_BIN_DIR/gitleaks"

    publish

    [ "$status" -eq 1 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=failed kind=transient reason=the secret scan failed"* ]]
    [ -z "$(push_lines)" ]
}

@test "publish: a clean run is scanned with Foundry's configuration over the run commits only" {
    start_run
    wcommit a.txt a "first"
    worker_bundle
    cat > "$FAKE_BIN_DIR/gitleaks" <<SCANNER
#!/bin/sh
printf '%s\n' "\$*" > "$BATS_TEST_TMPDIR/gitleaks_args"
exec "$(command -v gitleaks)" "\$@"
SCANNER
    chmod +x "$FAKE_BIN_DIR/gitleaks"

    publish

    [ "$status" -eq 0 ]
    local args
    args="$(cat "$BATS_TEST_TMPDIR/gitleaks_args")"
    [[ "$args" == *"--config $FOUNDRY_GITLEAKS_CONFIG"* ]]
    [[ "$args" == *"--ignore-gitleaks-allow"* ]]
    [[ "$args" == *"--gitleaks-ignore-path /dev/null"* ]]
    [[ "$args" == *"--redact"* ]]
    [[ "$args" == *"--not $BASE_DEFAULT_SHA $BASE_DEFAULT_SHA"* ]]
}

# ---------------------------------------------------------------------------
# Confirmed false positives (#899): CONFIRMED_FINDINGS suppresses exactly the
# findings an operator confirmed, by gitleaks fingerprint.
# ---------------------------------------------------------------------------

OTHER_GITHUB_PAT="ghp_$(printf '%s' 3kWq9ZtR7mXc2VbN5aLs8DfG4hJy1PoU6eIr)"

# fingerprints — the gitleaks fingerprint (<commit>:<path>:<rule>:<line>) of every finding the last publish reported.
fingerprints() {
    secret_findings | sed -n 's/^FOUNDRY_SECRET_FINDING commit=\([0-9a-f]*\) line=\([0-9]*\) rule=\([^ ]*\) path=\(.*\)$/\1:\4:\3:\2/p'
}

# confirm_and_publish_again <fingerprints> — publishes the same Run bundle again with <fingerprints> confirmed.
confirm_and_publish_again() {
    export CONFIRMED_FINDINGS="$1"
    : > "$BATS_TEST_TMPDIR/git_args"
    publish
}

@test "publish: a confirmed finding is suppressed and the run publishes" {
    start_run
    wcommit app.env "TOKEN=$GITHUB_PAT" "add config"
    worker_bundle
    publish
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=1" ]

    confirm_and_publish_again "$(fingerprints)"

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=published head=$(remote_sha "$RUN_BRANCH") commits=1" ]
    [ -z "$(secret_findings)" ]
    [ -n "$(push_lines)" ]
}

@test "publish: a finding that was not confirmed still blocks the push and is the only one reported" {
    start_run
    wcommit a.env "TOKEN=$GITHUB_PAT" "first"
    wcommit b.env "KEY=$AWS_KEY_ID" "second"
    worker_bundle
    publish
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=2" ]

    confirm_and_publish_again "$(fingerprints | grep ':a\.env:github-pat:')"

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=1" ]
    [ "$(secret_findings | wc -l)" -eq 1 ]
    secret_findings | grep -qE ' rule=aws-access-token path=b\.env$'
    [ -z "$(push_lines)" ]
}

@test "publish: a confirmed fingerprint does not suppress the same path, rule and line in another commit" {
    start_run
    wcommit app.env "TOKEN=$GITHUB_PAT" "placeholder"
    wcommit app.env "TOKEN=$OTHER_GITHUB_PAT" "real key"
    worker_bundle
    publish
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=2" ]
    local placeholder fingerprint
    while IFS= read -r fingerprint; do
        if [ "$("$REAL_GIT" -C "$FOUNDRY_WORK_DIR/repo" log -1 --format=%s "${fingerprint%%:*}")" = placeholder ]; then
            placeholder="$fingerprint"
        fi
    done <<< "$(fingerprints)"
    [ -n "$placeholder" ]

    confirm_and_publish_again "$placeholder"

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=1" ]
    secret_findings | grep -qE ' line=1 rule=github-pat path=app\.env$'
    [ "$(secret_findings | wc -l)" -eq 1 ]
    [[ "$(secret_findings)" != *"commit=${placeholder%%:*} "* ]]
    [ -z "$(push_lines)" ]
}

@test "publish: an empty CONFIRMED_FINDINGS suppresses nothing" {
    start_run
    wcommit app.env "TOKEN=$GITHUB_PAT" "add config"
    worker_bundle
    export CONFIRMED_FINDINGS=""

    publish

    [ "$status" -eq 0 ]
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=1" ]
    [ -z "$(push_lines)" ]
}

@test "publish: an unset or empty CONFIRMED_FINDINGS scans with no ignore file, as before" {
    start_run
    wcommit a.txt a "first"
    worker_bundle
    cat > "$FAKE_BIN_DIR/gitleaks" <<SCANNER
#!/bin/sh
printf '%s\n' "\$*" >> "$BATS_TEST_TMPDIR/gitleaks_args"
exec "$(command -v gitleaks)" "\$@"
SCANNER
    chmod +x "$FAKE_BIN_DIR/gitleaks"

    publish
    export CONFIRMED_FINDINGS=""
    publish

    [ "$status" -eq 0 ]
    [ "$(grep -c -- '--gitleaks-ignore-path /dev/null' "$BATS_TEST_TMPDIR/gitleaks_args")" -eq 2 ]
}

@test "publish: a confirmed finding in a path holding a space is suppressed" {
    start_run
    mkdir -p "$WORKER/my fixtures"
    wcommit "my fixtures/app.env" "TOKEN=$GITHUB_PAT" "add fixture"
    worker_bundle
    publish
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=1" ]

    confirm_and_publish_again "$(fingerprints)"

    [ "$status" -eq 0 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=published "* ]]
}

# Observed with gitleaks 8.30.1 on 2026-11-13: an ignore entry whose path holds ':' still matches its finding.
@test "publish: a confirmed finding in a path holding a colon is suppressed" {
    start_run
    wcommit "a:b.env" "TOKEN=$GITHUB_PAT" "add fixture"
    worker_bundle
    publish
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=secret_detected findings=1" ]

    confirm_and_publish_again "$(fingerprints)"

    [ "$status" -eq 0 ]
    [[ "$(result_line)" == "FOUNDRY_PUBLISH result=published "* ]]
}

@test "publish: a binary file the run added is removed by one code-authored revert commit and listed" {
    start_run
    write_binary assets/logo.png
    wcommit_bytes assets/logo.png "add logo"
    wcommit a.txt a "text work"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    local head
    head="$(remote_sha "$RUN_BRANCH")"
    [ "$(result_line)" = "FOUNDRY_PUBLISH result=published head=$head commits=3" ]
    [ "$(remote_git show -s --format=%s "$head")" = "chore: revert files blocked by the Foundry diff gate" ]
    [ "$(remote_git show -s --format='%an <%ae>|%cn <%ce>' "$head")" = "$ACCOUNT_NAME <$ACCOUNT_EMAIL>|$ACCOUNT_NAME <$ACCOUNT_EMAIL>" ]
    ! remote_git cat-file -e "$head:assets/logo.png" 2> /dev/null
    remote_git cat-file -e "$head:a.txt"
    [ "$(reverted_files)" = "FOUNDRY_REVERTED_FILE reason=binary size=$(wc -c < "$WORKER/assets/logo.png") path=assets/logo.png" ]
}

@test "publish: the revert commit is not listed among the run commits" {
    start_run
    write_binary logo.png
    wcommit_bytes logo.png "add logo"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(grep -c '^FOUNDRY_RUN_COMMIT ' <<< "$output")" -eq 1 ]
    grep -q '^FOUNDRY_RUN_COMMIT [0-9a-f]* add logo$' <<< "$output"
}

@test "publish: an oversized text file is reverted with reason oversized and its size" {
    start_run
    write_oversized data/dump.sql
    wcommit_bytes data/dump.sql "add dump"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(reverted_files)" = "FOUNDRY_REVERTED_FILE reason=oversized size=$((DIFF_GATE_MAX_FILE_BYTES + 1)) path=data/dump.sql" ]
    ! remote_git cat-file -e "$(remote_sha "$RUN_BRANCH"):data/dump.sql" 2> /dev/null
}

@test "publish: a file exactly at the size limit is published" {
    start_run
    head -c "$DIFF_GATE_MAX_FILE_BYTES" /dev/zero | tr '\0' 'a' > "$WORKER/limit.txt"
    wcommit_bytes limit.txt "at the limit"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ -z "$(reverted_files)" ]
    [ "$(remote_git show -s --format=%s "$(remote_sha "$RUN_BRANCH")")" = "at the limit" ]
}

@test "publish: a file both oversized and binary is reported once, as binary" {
    start_run
    { printf 'BIN\000'; head -c "$DIFF_GATE_MAX_FILE_BYTES" /dev/zero; } > "$WORKER/big.bin"
    wcommit_bytes big.bin "add big binary"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(reverted_files)" = "FOUNDRY_REVERTED_FILE reason=binary size=$((DIFF_GATE_MAX_FILE_BYTES + 4)) path=big.bin" ]
}

@test "publish: a changed file is restored to its content at the run's base" {
    add_remote_branch "$RUN_BRANCH_NAME"
    seed_commit "$RUN_BRANCH_NAME" notes.txt "original notes" "notes"
    start_run
    write_binary notes.txt
    wcommit_bytes notes.txt "make notes binary"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(remote_git show "$(remote_sha "$RUN_BRANCH"):notes.txt")" = "original notes" ]
    reverted_files | grep -qx "FOUNDRY_REVERTED_FILE reason=binary size=[0-9]* path=notes.txt"
}

@test "publish: a rebased run restores a reverted file to the default branch, keeping the default branch's change" {
    seed_commit main shared.txt "main v1" "shared v1"
    add_remote_branch "$RUN_BRANCH_NAME"
    seed_commit main shared.txt "main v2" "shared v2"
    start_run
    wgit rebase --quiet origin/HEAD
    write_binary shared.txt
    wcommit_bytes shared.txt "make shared binary"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ "$(remote_git show "$(remote_sha "$RUN_BRANCH"):shared.txt")" = "main v2" ]
}

@test "publish: a binary file the run deleted is not gated" {
    seed_commit main old.bin "placeholder" "old"
    local seed="$BATS_TEST_TMPDIR/seed"
    printf 'X\000Y' > "$seed/old.bin"
    "$REAL_GIT" -C "$seed" add old.bin
    "$REAL_GIT" -C "$seed" -c user.name=t -c user.email=t@t commit --quiet -m "binary on main"
    "$REAL_GIT" -C "$seed" push --quiet "$REMOTE" HEAD:refs/heads/main
    start_run
    wgit rm --quiet old.bin
    wgit commit --quiet -m "remove binary"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ -z "$(reverted_files)" ]
    [ "$(remote_git show -s --format=%s "$(remote_sha "$RUN_BRANCH")")" = "remove binary" ]
}

@test "publish: a binary file on the default branch that the run merged in is not reverted" {
    add_remote_branch "$RUN_BRANCH_NAME"
    local seed="$BATS_TEST_TMPDIR/seed"
    "$REAL_GIT" -C "$seed" fetch --quiet "$REMOTE" "+refs/heads/*:refs/remotes/r/*"
    "$REAL_GIT" -C "$seed" switch --quiet --detach r/main
    printf 'X\000Y' > "$seed/main.bin"
    "$REAL_GIT" -C "$seed" add main.bin
    "$REAL_GIT" -C "$seed" -c user.name=t -c user.email=t@t commit --quiet -m "binary on main"
    "$REAL_GIT" -C "$seed" push --quiet "$REMOTE" HEAD:refs/heads/main
    start_run
    wgit merge --quiet --no-edit origin/HEAD
    wcommit a.txt a "run work"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ -z "$(reverted_files)" ]
    remote_git cat-file -e "$(remote_sha "$RUN_BRANCH"):main.bin"
}

@test "publish: CI/CD files are published like any other file" {
    start_run
    mkdir -p "$WORKER/.github/workflows"
    wcommit .github/workflows/ci.yml "on: push" "ci"
    wcommit .gitlab-ci.yml "stages: [build]" "gitlab ci"
    wcommit Dockerfile "FROM alpine" "dockerfile"
    wcommit docker-compose.dev.yml "services: {}" "compose"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    [ -z "$(reverted_files)" ]
    [ "$(remote_git show -s --format=%s "$(remote_sha "$RUN_BRANCH")")" = "compose" ]
}

@test "publish: a run whose only change is reverted still pushes the revert commit" {
    start_run
    write_binary only.bin
    wcommit_bytes only.bin "only a binary"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    local head
    head="$(remote_sha "$RUN_BRANCH")"
    [ "$(remote_git show -s --format=%s "$head")" = "chore: revert files blocked by the Foundry diff gate" ]
    [ "$(remote_git rev-parse "$head^{tree}")" = "$(remote_git rev-parse "main^{tree}")" ]
}

@test "publish: a path with spaces is reverted and reported whole" {
    start_run
    write_binary "docs/my image.png"
    wcommit_bytes "docs/my image.png" "add image"
    worker_bundle

    publish

    [ "$status" -eq 0 ]
    reverted_files | grep -qx "FOUNDRY_REVERTED_FILE reason=binary size=[0-9]* path=docs/my image.png"
    ! remote_git cat-file -e "$(remote_sha "$RUN_BRANCH"):docs/my image.png" 2> /dev/null
}

@test "publish: publishing a run with reverted files again is idempotent" {
    start_run
    write_binary logo.png
    wcommit_bytes logo.png "add logo"
    worker_bundle
    publish
    [ "$status" -eq 0 ]
    local first
    first="$output"

    publish

    [ "$status" -eq 0 ]
    [ "$output" = "$first" ]
    [ "$(push_lines | wc -l)" -eq 1 ]
}

# ---------------------------------------------------------------------------
# Probe mode (host pause)
#
# One ls-remote HEAD; unreachable is exactly classify_error's unreachable and every other outcome is reachable.
# ---------------------------------------------------------------------------

probe_line() {
    grep '^FOUNDRY_PROBE ' <<< "$output"
}

# probe_reports <result> <stderr> — the provider fails the probe's ls-remote once with <stderr>, and the probe
# reports <result> carrying it, after exactly one attempt and no sleep.
probe_reports() {
    provider_errors "$2" "$2"

    run sh "$PUBLISHER" probe

    [ "$status" -eq 0 ]
    [ "$(probe_line)" = "FOUNDRY_PROBE result=$1 detail=$2" ]
    [ "$(ls_remote_count)" -eq 1 ]
    [ -z "$(sleeps)" ]
}

@test "probe: a provider that answers is reachable, on one FOUNDRY_PROBE line" {
    run sh "$PUBLISHER" probe

    [ "$status" -eq 0 ]
    [ "$(grep -c '^FOUNDRY_PROBE ' <<< "$output")" -eq 1 ]
    [[ "$(probe_line)" == "FOUNDRY_PROBE result=reachable detail="* ]]
}

@test "probe: runs ls-remote HEAD once against CLONE_URL" {
    run sh "$PUBLISHER" probe

    [ "$status" -eq 0 ]
    [ "$(ls_remote_count)" -eq 1 ]
    grep -q " ls-remote $AUTH_URL HEAD\$" "$BATS_TEST_TMPDIR/git_args"
}

@test "probe: a DNS failure is unreachable, without a retry" {
    probe_reports unreachable "$GIT_DNS_FAILURE"
}

@test "probe: a refused connection is unreachable" {
    probe_reports unreachable "$GIT_CONNECTION_REFUSED"
}

@test "probe: an HTTP 503 is unreachable" {
    probe_reports unreachable "$GIT_HTTP_503"
}

@test "probe: an HTTP 429 is unreachable" {
    probe_reports unreachable "$GIT_HTTP_429"
}

@test "probe: an HTTP 403 is reachable: the provider answered" {
    probe_reports reachable "$GIT_HTTP_403"
}

@test "probe: git finding no credentials is reachable" {
    probe_reports reachable "$GIT_NO_CREDENTIALS"
}

@test "probe: a rate limit without git's transport prefix is reachable" {
    probe_reports reachable "remote: API rate limit exceeded"
}

@test "probe: an authentication failure is reachable" {
    export FAKE_GIT_AUTH_FAILED=1

    run sh "$PUBLISHER" probe

    [ "$status" -eq 0 ]
    [[ "$(probe_line)" == "FOUNDRY_PROBE result=reachable detail=fatal: Authentication failed"* ]]
}

@test "probe: the token reaches git only through GIT_ASKPASS, never an argument or the output" {
    export FAKE_GIT_UNREACHABLE=1

    run sh "$PUBLISHER" probe

    [ "$status" -eq 0 ]
    [ "$(cat "$BATS_TEST_TMPDIR/askpass_answer")" = "ghp_publisher_token" ]
    ! grep -q "ghp_publisher_token" "$BATS_TEST_TMPDIR/git_args"
    [[ "$output" != *"ghp_publisher_token"* ]]
}

@test "probe: an error naming the token prints it masked" {
    provider_errors "fatal: unable to access 'https://oauth2:ghp_publisher_token@git.example.test/': Could not resolve host"

    run sh "$PUBLISHER" probe

    [ "$status" -eq 0 ]
    [[ "$output" != *"ghp_publisher_token"* ]]
    [[ "$(probe_line)" == "FOUNDRY_PROBE result=unreachable detail="*"***"* ]]
}

@test "probe: git runs with no inherited configuration, in a fresh scratch repository with hooks off" {
    printf '[url "https://evil.example.test/"]\n\tinsteadOf = https://\n' > "$BATS_TEST_TMPDIR/gitconfig"
    export GIT_CONFIG_GLOBAL="$BATS_TEST_TMPDIR/gitconfig"

    run sh "$PUBLISHER" probe

    [ "$status" -eq 0 ]
    [ "$(sort -u "$BATS_TEST_TMPDIR/git_env")" = "GIT_CONFIG_NOSYSTEM=1 GIT_CONFIG_GLOBAL=/dev/null" ]
    grep -q -- "-c core.hooksPath=/dev/null -c credential.helper= ls-remote" "$BATS_TEST_TMPDIR/git_args"
}

@test "probe: the askpass helper is removed when the probe exits" {
    export TMPDIR="$BATS_TEST_TMPDIR/tmp"
    mkdir -p "$TMPDIR"

    run sh "$PUBLISHER" probe

    [ "$status" -eq 0 ]
    [ -z "$(ls -A "$TMPDIR")" ]
}

@test "probe: a missing GIT_PAT prints no FOUNDRY_PROBE line and makes no git transfer" {
    unset GIT_PAT

    run sh "$PUBLISHER" probe

    [ "$status" -ne 0 ]
    [[ "$output" != *"FOUNDRY_PROBE"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/git_args" ]
}

@test "probe: a CLONE_URL that is not https prints no FOUNDRY_PROBE line and makes no git transfer" {
    export CLONE_URL="git@git.example.test:owner/repo.git"

    run sh "$PUBLISHER" probe

    [ "$status" -ne 0 ]
    [[ "$output" != *"FOUNDRY_PROBE"* ]]
    [ ! -f "$BATS_TEST_TMPDIR/git_args" ]
}
