#!/bin/sh
# The Publisher (ADR 0100): a short-lived, model-free container that holds the provider token for one
# worker run and performs every git transfer with the provider, so no worker container spec carries a
# provider credential.
#
# Modes:
#   prepare — fetch the default branch (and RUN_BRANCH, when set) into a fresh repository and write
#             them to <handoff>/in/base.bundle, then print the run's base on one stdout line:
#               FOUNDRY_BASE default_branch=<name> default_sha=<sha> [branch_sha=<sha>]
#
# Every failure prints the bootstrap-failure sentinel Foundry already classifies, on stderr:
#   FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=<detail>
#
# Environment:
#   CLONE_URL   — the repository's https clone URL (required)
#   GIT_PAT     — the provider token, delivered to git only through GIT_ASKPASS (required)
#   RUN_BRANCH  — the run's existing branch (Continuation and Revision runs only)
# FOUNDRY_HANDOFF_DIR and FOUNDRY_WORK_DIR redirect /handoff and /work for the bats suite only.
set -eu

HANDOFF_DIR="${FOUNDRY_HANDOFF_DIR:-/handoff}"
WORK_DIR="${FOUNDRY_WORK_DIR:-/work}"

# Never inherit git configuration: a system or global file could add hooks, an fsmonitor, a credential
# helper or a url.*.insteadOf rewrite that runs or redirects with the token present.
GIT_CONFIG_NOSYSTEM=1
GIT_CONFIG_GLOBAL=/dev/null
GIT_TERMINAL_PROMPT=0
export GIT_CONFIG_NOSYSTEM GIT_CONFIG_GLOBAL GIT_TERMINAL_PROMPT
unset GIT_CONFIG GIT_CONFIG_PARAMETERS GIT_CONFIG_COUNT GIT_DIR GIT_WORK_TREE GIT_ASKPASS SSH_ASKPASS

# ---------------------------------------------------------------------------
# fail <detail>
#
# Prints the bootstrap-failure sentinel (collapsed to one line, so Foundry's line-oriented scan finds it
# whole) and exits non-zero.
# ---------------------------------------------------------------------------
fail() {
    detail="$(printf '%s' "$1" | tr '\n' ' ')"
    printf 'FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=%s\n' "$detail" >&2
    exit 1
}

# ---------------------------------------------------------------------------
# make_clone_url <clone_url>
#
# Converts a plain HTTPS clone URL into one with the oauth2 username understood by both GitHub and
# GitLab: https://host/path[.git] → https://oauth2@host/path.git. The token is NOT embedded in the URL;
# GIT_ASKPASS delivers it.
# ---------------------------------------------------------------------------
make_clone_url() {
    case "$1" in
        https://*) ;;
        *)
            echo "ERROR: make_clone_url requires an https:// URL; got: $1" >&2
            return 1
            ;;
    esac
    with_user="https://oauth2@${1#https://}"
    case "$with_user" in
        *.git) ;;
        *) with_user="${with_user}.git" ;;
    esac
    printf '%s' "$with_user"
}

# ---------------------------------------------------------------------------
# make_askpass_script
#
# The body of the GIT_ASKPASS helper: it prints $GIT_PAT when git asks for the password. printf avoids
# a trailing newline that some git versions misread.
# ---------------------------------------------------------------------------
make_askpass_script() {
    printf '%s' 'printf "%s" "$GIT_PAT"'
}

# is_branch_name <name> — the branch-name characters the worker entrypoint also accepts; anything else
# could break the refspecs or the FOUNDRY_BASE line.
is_branch_name() {
    printf '%s' "$1" | grep -Eqx '[a-zA-Z0-9_/.-]+'
}

# remote_sha <ref> — the SHA ls-remote advertised for exactly <ref>, or nothing.
remote_sha() {
    printf '%s\n' "$refs" | awk -F '\t' -v ref="$1" '$2 == ref && $1 ~ /^[0-9a-f]+$/ { print $1; exit }'
}

# last_error — the last non-empty line git wrote, which names its failure (e.g. "fatal: Authentication
# failed").
last_error() {
    grep -v '^[[:space:]]*$' "$git_err" 2> /dev/null | tail -n 1 || true
}

# git in the scratch repository, with the token offered only through GIT_ASKPASS and hooks off whatever
# state the repository acquires.
provider_git() {
    GIT_ASKPASS="$ASKPASS_FILE" git -C "$REPO_DIR" -c core.hooksPath=/dev/null -c credential.helper= "$@"
}

prepare() {
    : "${CLONE_URL:?CLONE_URL is required}"
    : "${GIT_PAT:?GIT_PAT is required}"
    run_branch="${RUN_BRANCH:-}"

    if [ -n "$run_branch" ] && ! is_branch_name "$run_branch"; then
        fail "invalid RUN_BRANCH characters"
    fi

    auth_url="$(make_clone_url "$CLONE_URL")" || fail "CLONE_URL must be an https:// URL"

    ASKPASS_FILE="$(mktemp)"
    git_err="$(mktemp)"
    trap 'rm -f "$ASKPASS_FILE" "$git_err"' EXIT
    printf '%s\n' "#!/bin/sh" "$(make_askpass_script)" > "$ASKPASS_FILE"
    chmod 700 "$ASKPASS_FILE"

    REPO_DIR="$WORK_DIR/repo"
    rm -rf "$REPO_DIR"
    # An empty template: the repository starts with no sample hooks or other template content.
    git init --quiet --bare --template= "$REPO_DIR" 2> "$git_err" \
        || fail "could not create the scratch repository: $(last_error)"
    git -C "$REPO_DIR" config core.hooksPath /dev/null

    # ls-remote prints "ref: refs/heads/<default><TAB>HEAD", then "<sha><TAB><ref>" lines.
    if [ -n "$run_branch" ]; then
        refs="$(provider_git ls-remote --symref "$auth_url" HEAD "refs/heads/$run_branch" 2> "$git_err")" \
            || fail "could not reach the repository: $(last_error)"
    else
        refs="$(provider_git ls-remote --symref "$auth_url" HEAD 2> "$git_err")" \
            || fail "could not reach the repository: $(last_error)"
    fi

    default_branch="$(printf '%s\n' "$refs" \
        | awk -F '\t' '$2 == "HEAD" && sub(/^ref: refs\/heads\//, "", $1) { print $1; exit }')"
    if [ -z "$default_branch" ] || [ -z "$(remote_sha HEAD)" ]; then
        fail "the repository has no default branch to clone (is it empty?)"
    fi
    if ! is_branch_name "$default_branch"; then
        fail "the repository's default branch name has unsupported characters"
    fi

    refspecs="+refs/heads/$default_branch:refs/heads/$default_branch"
    bundle_refs="HEAD refs/heads/$default_branch"
    if [ -n "$run_branch" ]; then
        if [ -z "$(remote_sha "refs/heads/$run_branch")" ]; then
            fail "the run's branch $run_branch does not exist on the remote"
        fi
        if [ "$run_branch" != "$default_branch" ]; then
            refspecs="$refspecs +refs/heads/$run_branch:refs/heads/$run_branch"
            bundle_refs="$bundle_refs refs/heads/$run_branch"
        fi
    fi

    # shellcheck disable=SC2086 # refspecs holds whitespace-free refspecs.
    provider_git fetch --quiet --no-tags "$auth_url" $refspecs 2> "$git_err" \
        || fail "fetching the repository failed: $(last_error)"
    git -C "$REPO_DIR" symbolic-ref HEAD "refs/heads/$default_branch"

    default_sha="$(git -C "$REPO_DIR" rev-parse --verify "refs/heads/$default_branch^{commit}")"
    branch_sha=""
    if [ -n "$run_branch" ]; then
        branch_sha="$(git -C "$REPO_DIR" rev-parse --verify "refs/heads/$run_branch^{commit}")"
    fi

    # Written beside its final name and renamed, so a reader never sees a partial bundle. World-readable:
    # the worker reads it as a different user.
    mkdir -p "$HANDOFF_DIR/in"
    bundle_tmp="$HANDOFF_DIR/in/.base.bundle.tmp"
    # shellcheck disable=SC2086 # bundle_refs holds whitespace-free ref names.
    git -C "$REPO_DIR" bundle create "$bundle_tmp" $bundle_refs > /dev/null 2> "$git_err" \
        || fail "writing the base bundle failed: $(last_error)"
    chmod 0644 "$bundle_tmp"
    mv -f "$bundle_tmp" "$HANDOFF_DIR/in/base.bundle"

    if [ -n "$branch_sha" ]; then
        printf 'FOUNDRY_BASE default_branch=%s default_sha=%s branch_sha=%s\n' "$default_branch" "$default_sha" "$branch_sha"
    else
        printf 'FOUNDRY_BASE default_branch=%s default_sha=%s\n' "$default_branch" "$default_sha"
    fi
}

case "${1:-}" in
    prepare) prepare ;;
    *)
        echo "usage: publisher.sh prepare" >&2
        exit 64
        ;;
esac
