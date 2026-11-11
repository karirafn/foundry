#!/bin/sh
# The Publisher (ADR 0100): a short-lived, model-free container that holds the provider token for one
# worker run and performs every git transfer with the provider, so no worker container spec carries a
# provider credential.
#
# Modes:
#   prepare — fetch the default branch (and RUN_BRANCH, when set) into a fresh repository and write
#             them to <handoff>/in/base.bundle, create <handoff>/out writable for the worker, then
#             print the run's base on one stdout line:
#               FOUNDRY_BASE default_branch=<name> default_sha=<sha> [branch_sha=<sha>]
#             Every failure prints the bootstrap-failure sentinel Foundry already classifies, on stderr:
#               FOUNDRY_BOOTSTRAP_FAILED stage=clone detail=<detail>
#   publish — push the worker's Run bundle (<handoff>/out/run.bundle) from a fresh repository; see
#             publish() below for the steps and the FOUNDRY_PUBLISH result line.
#
# Environment (both modes):
#   CLONE_URL   — the repository's https clone URL (required)
#   GIT_PAT     — the provider token, delivered to git only through GIT_ASKPASS (required)
#   RUN_BRANCH  — the run's branch (prepare: existing branches only; publish: required)
# Publish mode only:
#   RUN_ID           — the worker run's id, [a-zA-Z0-9-]+ (names the side branch <branch>-run-<id>)
#   BASE_DEFAULT_SHA — the default-branch head recorded at Prepare (D)
#   BASE_BRANCH_SHA  — the run-branch head recorded at Prepare; empty for a Fresh run
#   AUTHOR_NAME, AUTHOR_EMAIL — the account identity every Run commit is rewritten to
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

    # The worker writes its Run bundle to out/ as another user with no capability to chown or bypass
    # permissions, so the directory is made world-writable here, by its owner.
    { mkdir -p "$HANDOFF_DIR/out" && chmod 0777 "$HANDOFF_DIR/out"; } 2> "$git_err" \
        || fail "could not create the run bundle directory: $(last_error)"

    if [ -n "$branch_sha" ]; then
        printf 'FOUNDRY_BASE default_branch=%s default_sha=%s branch_sha=%s\n' "$default_branch" "$default_sha" "$branch_sha"
    else
        printf 'FOUNDRY_BASE default_branch=%s default_sha=%s\n' "$default_branch" "$default_sha"
    fi
}

# ===========================================================================
# Publish mode (ADR 0100, ADR 0101)
#
# Pushes the worker's Run bundle from a fresh repository. The Hand-off volume is read for exactly one
# file, out/run.bundle, as data: git never runs in agent-written state while the token is present.
#
# Stdout carries, last, exactly one result line Foundry parses:
#   FOUNDRY_PUBLISH result=published head=<sha> commits=<n>
#   FOUNDRY_PUBLISH result=nothing_to_publish
#   FOUNDRY_PUBLISH result=diverged branch=<branch> side_branch=<side> head=<sha>[ reason=force-push-protected]
#   FOUNDRY_PUBLISH result=failed kind=permanent|transient reason=<one line; the last field, may hold spaces>
# preceded, for published and diverged, by one line per pushed Run commit, oldest first (Foundry's own
# merge commit excluded):
#   FOUNDRY_RUN_COMMIT <sha> <subject>
# <n> counts the commits on the pushed branch beyond the default branch. Diagnostics go to stderr.
# Exit status: 0 for published, nothing_to_publish and diverged; 1 for failed.
# ===========================================================================

# redact — copies stdin to stdout with every occurrence of $GIT_PAT masked.
redact() {
    awk 'BEGIN { t = ENVIRON["GIT_PAT"] }
        {
            if (t != "") {
                while ((i = index($0, t)) > 0) {
                    $0 = substr($0, 1, i - 1) "***" substr($0, i + length(t))
                }
            }
            print
        }'
}

# one_line <text> — <text> redacted and collapsed to a single line.
one_line() {
    printf '%s\n' "$1" | redact | tr '\r\n\t' '   ' | sed 's/  */ /g; s/^ //; s/ $//'
}

publish_failed() {
    printf 'FOUNDRY_PUBLISH result=failed kind=%s reason=%s\n' "$1" "$(one_line "$2")"
    exit 1
}

publish_invalid() {
    publish_failed permanent "invalid: $1"
}

publish_nothing() {
    echo "Nothing to publish: $1" >&2
    printf 'FOUNDRY_PUBLISH result=nothing_to_publish\n'
    exit 0
}

# publish_published <head> — the run commit lines, then the published result.
publish_published() {
    commits="$(scratch_git rev-list --count "$1" --not refs/foundry/default)"
    cat "$run_commit_lines"
    printf 'FOUNDRY_PUBLISH result=published head=%s commits=%s\n' "$1" "$commits"
    exit 0
}

# publish_diverged <head> [reason] — pushes <head> to the side branch, then reports diverged.
publish_diverged() {
    echo "Pushing the run to the side branch $side_branch" >&2
    if ! push_ref "$1" "$side_branch"; then
        fail_push "pushing the side branch $side_branch failed"
    fi
    cat "$run_commit_lines"
    if [ -n "${2:-}" ]; then
        printf 'FOUNDRY_PUBLISH result=diverged branch=%s side_branch=%s head=%s reason=%s\n' "$run_branch" "$side_branch" "$1" "$2"
    else
        printf 'FOUNDRY_PUBLISH result=diverged branch=%s side_branch=%s head=%s\n' "$run_branch" "$side_branch" "$1"
    fi
    exit 0
}

# classify_error <text> — "transient" or "permanent" for a git transfer failure. Unknown failures are
# transient, so Foundry's bounded retry gets a chance before the run needs attention.
classify_error() {
    lower="$(printf '%s' "$1" | tr '[:upper:]' '[:lower:]')"
    case "$lower" in
        *"rate limit"*|*"returned error: 429"*) echo transient ;;
        *"authentication failed"*|*"invalid username or password"*|*"invalid username or token"*|*"returned error: 401"*|*"returned error: 403"*|*"http 401"*|*"http 403"*|*"repository not found"*|*"returned error: 404"*|*"not our ref"*|*"invalid input"*|*"permission denied"*|*"access denied"*)
            echo permanent ;;
        *) echo transient ;;
    esac
}

# fail_git <context> — fails with the last git error, classified.
fail_git() {
    publish_failed "$(classify_error "$(cat "$git_err" 2> /dev/null)")" "$1: $(last_error)"
}

# fail_push <context> — fails after a push: a provider refusal (a protected branch, a hook) is
# permanent; a ref that moved under the push is transient (the retry merges); others are classified.
fail_push() {
    if grep -q '\[remote rejected\]' "$git_err"; then
        publish_failed permanent "$1: the provider refused the push: $(last_error)"
    fi
    if grep -q '\[rejected\]' "$git_err"; then
        publish_failed transient "$1: the remote branch moved during publish: $(last_error)"
    fi
    fail_git "$1"
}

# git in the scratch repository with hooks off, never touching the network.
scratch_git() {
    git -C "$REPO_DIR" -c core.hooksPath=/dev/null "$@"
}

# push_ref <sha> <branch> [<expected_sha>] — pushes <sha> to refs/heads/<branch> of the remote, fast-forward
# only; with <expected_sha>, a forced update leased on the remote branch still being <expected_sha>
# (ADR 0101). Never `+` or --force. git's output lands in $git_err (and on stderr).
push_ref() {
    if [ -n "${3:-}" ]; then
        provider_git push "--force-with-lease=refs/heads/$2:$3" "$auth_url" "$1:refs/heads/$2" \
            > "$git_err" 2>&1 && status=0 || status=$?
    else
        provider_git push "$auth_url" "$1:refs/heads/$2" > "$git_err" 2>&1 && status=0 || status=$?
    fi
    redact < "$git_err" >&2
    return "$status"
}

is_sha() {
    printf '%s' "$1" | grep -Eqx '[0-9a-f]{40}([0-9a-f]{24})?'
}

# is_identity <value> — non-empty and free of the characters that would break a commit header.
is_identity() {
    [ -n "$1" ] || return 1
    case "$1" in
        *'<'*|*'>'*|*"$NEWLINE"*) return 1 ;;
    esac
    return 0
}

# rewrite_commit <commit_object_file> — prints the commit object rewritten for publishing:
#   - author and committer become AUTHOR_NAME <AUTHOR_EMAIL>, their dates kept;
#   - each parent already rewritten is replaced through $commit_map ("<old> <new>" lines); a parent
#     outside the Run commits (a default-branch commit merged in) keeps its SHA;
#   - a signature (gpgsig) is dropped: it no longer matches;
#   - every Co-Authored-By trailer (key matched case-insensitively) and every "Generated with
#     [Claude Code](...)" footer, with or without the robot emoji, is removed from the message, and
#     trailing blank lines left behind are trimmed. A message left empty keeps its subject.
# LC_ALL=C makes awk compare bytes, so the emoji matches whatever the message's encoding.
rewrite_commit() {
    LC_ALL=C ROBOT="$(printf '\360\237\244\226')" VS16="$(printf '\357\270\217')" awk -v mapfile="$commit_map" '
        function is_attribution(line,   s) {
            s = tolower(line)
            if (s ~ /^[ \t]*co-authored-by[ \t]*:/) {
                return 1
            }
            s = line
            sub(/^[ \t]+/, "", s)
            if (substr(s, 1, length(robot)) == robot) {
                s = substr(s, length(robot) + 1)
                if (substr(s, 1, length(vs16)) == vs16) {
                    s = substr(s, length(vs16) + 1)
                }
                sub(/^[ \t]+/, "", s)
            }
            return s ~ /^Generated with \[Claude Code\]\([^)]*\)[ \t]*$/
        }
        BEGIN {
            identity = ENVIRON["AUTHOR_NAME"] " <" ENVIRON["AUTHOR_EMAIL"] ">"
            robot = ENVIRON["ROBOT"]
            vs16 = ENVIRON["VS16"]
            in_header = 1
            in_signature = 0
            n = 0
        }
        FILENAME == mapfile {
            map[$1] = $2
            next
        }
        in_header {
            if ($0 == "") {
                in_header = 0
                print
                next
            }
            if (in_signature && substr($0, 1, 1) == " ") {
                next
            }
            in_signature = 0
            if ($1 == "gpgsig" || $1 == "gpgsig-sha256") {
                in_signature = 1
                next
            }
            if ($1 == "parent" && ($2 in map)) {
                print "parent " map[$2]
                next
            }
            if ($1 == "author" || $1 == "committer") {
                print $1 " " identity " " $(NF - 1) " " $NF
                next
            }
            print
            next
        }
        {
            message[++n] = $0
        }
        END {
            kept = 0
            for (i = 1; i <= n; i++) {
                if (!is_attribution(message[i])) {
                    out[++kept] = message[i]
                }
            }
            while (kept > 0 && out[kept] ~ /^[ \t]*$/) {
                kept--
            }
            if (kept == 0) {
                for (i = 1; i <= n; i++) {
                    if (message[i] !~ /^[ \t]*$/) {
                        out[++kept] = message[i]
                        break
                    }
                }
            }
            for (i = 1; i <= kept; i++) {
                print out[i]
            }
        }
    ' "$commit_map" "$1"
}

# map_sha <old> — the rewritten SHA of <old>.
map_sha() {
    awk -v old="$1" '$1 == old { print $2; exit }' "$commit_map"
}

publish() {
    NEWLINE='
'
    run_branch="${RUN_BRANCH:-}"
    run_id="${RUN_ID:-}"
    base_default="${BASE_DEFAULT_SHA:-}"
    base_branch="${BASE_BRANCH_SHA:-}"

    # --- Validate every input before any transfer --------------------------------------------------
    [ -n "${CLONE_URL:-}" ] || publish_invalid "CLONE_URL is required"
    [ -n "${GIT_PAT:-}" ] || publish_invalid "GIT_PAT is required"
    if ! is_branch_name "$run_branch" || ! git check-ref-format "refs/heads/$run_branch"; then
        publish_invalid "RUN_BRANCH is not a valid branch name"
    fi
    printf '%s' "$run_id" | grep -Eqx '[a-zA-Z0-9-]+' || publish_invalid "RUN_ID must match [a-zA-Z0-9-]+"
    side_branch="$run_branch-run-$run_id"
    git check-ref-format "refs/heads/$side_branch" || publish_invalid "the side branch name is not valid"
    is_sha "$base_default" || publish_invalid "BASE_DEFAULT_SHA is not a commit SHA"
    if [ -n "$base_branch" ] && ! is_sha "$base_branch"; then
        publish_invalid "BASE_BRANCH_SHA is not a commit SHA"
    fi
    is_identity "${AUTHOR_NAME:-}" || publish_invalid "AUTHOR_NAME is empty or has unsupported characters"
    is_identity "${AUTHOR_EMAIL:-}" || publish_invalid "AUTHOR_EMAIL is empty or has unsupported characters"
    case "$AUTHOR_EMAIL" in
        *' '*) publish_invalid "AUTHOR_EMAIL is empty or has unsupported characters" ;;
    esac
    auth_url="$(make_clone_url "$CLONE_URL" 2> /dev/null)" || publish_invalid "CLONE_URL must be an https:// URL"

    # --- The Run bundle: the only file read from the Hand-off volume --------------------------------
    run_bundle="$HANDOFF_DIR/out/run.bundle"
    if [ ! -e "$run_bundle" ] && [ ! -L "$run_bundle" ]; then
        publish_nothing "the run left no run bundle"
    fi
    if [ -L "$run_bundle" ] || [ ! -f "$run_bundle" ]; then
        publish_invalid "the run bundle is not a regular file"
    fi

    ASKPASS_FILE="$(mktemp)"
    git_err="$(mktemp)"
    commit_map="$(mktemp)"
    commit_object="$(mktemp)"
    rewritten_object="$(mktemp)"
    run_commit_list="$(mktemp)"
    run_commit_lines="$(mktemp)"
    trap 'rm -f "$ASKPASS_FILE" "$git_err" "$commit_map" "$commit_object" "$rewritten_object" "$run_commit_list" "$run_commit_lines"' EXIT
    printf '%s\n' "#!/bin/sh" "$(make_askpass_script)" > "$ASKPASS_FILE"
    chmod 700 "$ASKPASS_FILE"

    # --- A fresh scratch repository with safe configuration ----------------------------------------
    REPO_DIR="$WORK_DIR/repo"
    rm -rf "$REPO_DIR"
    git init --quiet --bare --template= "$REPO_DIR" 2> "$git_err" \
        || publish_failed transient "could not create the scratch repository: $(last_error)"
    git -C "$REPO_DIR" config core.hooksPath /dev/null

    # --- The remote's run branch and default branch, into Foundry-owned refs ------------------------
    refs="$(provider_git ls-remote --symref "$auth_url" HEAD "refs/heads/$run_branch" 2> "$git_err")" \
        || fail_git "could not reach the repository"
    default_branch="$(printf '%s\n' "$refs" \
        | awk -F '\t' '$2 == "HEAD" && sub(/^ref: refs\/heads\//, "", $1) { print $1; exit }')"
    if [ -z "$default_branch" ] || ! is_branch_name "$default_branch"; then
        publish_failed permanent "the repository has no usable default branch"
    fi
    if [ "$default_branch" = "$run_branch" ]; then
        publish_invalid "RUN_BRANCH is the default branch"
    fi
    refspecs="+refs/heads/$default_branch:refs/foundry/default"
    if [ -n "$(remote_sha "refs/heads/$run_branch")" ]; then
        refspecs="$refspecs +refs/heads/$run_branch:refs/foundry/remote-branch"
    fi
    # shellcheck disable=SC2086 # refspecs holds whitespace-free refspecs.
    provider_git fetch --quiet --no-tags "$auth_url" $refspecs 2> "$git_err" \
        || fail_git "fetching the repository failed"

    # The base commits recorded at Prepare are normally ancestors of what was just fetched; one that a
    # force push removed from the branches is fetched by SHA.
    for base_sha in "$base_default" "$base_branch"; do
        [ -n "$base_sha" ] || continue
        if ! scratch_git cat-file -e "$base_sha^{commit}" 2> /dev/null; then
            provider_git fetch --quiet --no-tags "$auth_url" "$base_sha:refs/foundry/base-$base_sha" 2> "$git_err" \
                || fail_git "the run's base commit $base_sha is not on the remote"
        fi
    done

    # --- Verify the bundle and fetch its tip into a Foundry-owned ref --------------------------------
    scratch_git bundle verify "$run_bundle" > /dev/null 2> "$git_err" \
        || publish_invalid "the run bundle failed verification: $(last_error)"
    bundle_heads="$(scratch_git bundle list-heads "$run_bundle" 2> "$git_err")" \
        || publish_invalid "the run bundle's heads cannot be read: $(last_error)"
    tip_ref="$(printf '%s\n' "$bundle_heads" | awk '$2 == "HEAD" { print $2; exit }')"
    if [ -z "$tip_ref" ]; then
        if [ "$(printf '%s\n' "$bundle_heads" | grep -c .)" -ne 1 ]; then
            publish_invalid "the run bundle does not have a single tip"
        fi
        tip_ref="$(printf '%s\n' "$bundle_heads" | awk '{ print $2 }')"
    fi
    printf '%s' "$tip_ref" | grep -Eqx 'HEAD|refs/[A-Za-z0-9._/-]+' \
        || publish_invalid "the run bundle's tip has an unsupported ref name"
    scratch_git -c fetch.fsckObjects=true fetch --quiet --no-tags "$run_bundle" "$tip_ref:refs/foundry/run-tip" 2> "$git_err" \
        || publish_invalid "the run bundle cannot be fetched: $(last_error)"
    tip="$(scratch_git rev-parse --verify --quiet "refs/foundry/run-tip^{commit}")" \
        || publish_invalid "the run bundle's tip is not a commit"

    # --- Ancestry (ADR 0101): forward from the run's base, or rebased onto the default branch --------
    if [ -n "$base_branch" ]; then
        base="$base_branch"
    else
        base="$base_default"
    fi
    if scratch_git merge-base --is-ancestor "$base" "$tip"; then
        tip_kind=forward
    elif [ -n "$base_branch" ] && scratch_git merge-base --is-ancestor "$base_default" "$tip"; then
        tip_kind=rebased
    else
        publish_invalid "run tip does not descend from the run's base"
    fi
    echo "Run tip $tip is $tip_kind" >&2

    # --- Run commits: tip --not <base> <D>, rewritten oldest first ------------------------------------
    scratch_git rev-list --topo-order --reverse "$tip" --not "$base" "$base_default" > "$run_commit_list" \
        || publish_failed transient "listing the run commits failed"
    if [ ! -s "$run_commit_list" ]; then
        publish_nothing "the run bundle holds no commits beyond the run's base"
    fi

    : > "$commit_map"
    : > "$run_commit_lines"
    while read -r old_sha; do
        scratch_git cat-file commit "$old_sha" > "$commit_object" \
            || publish_invalid "the run commit $old_sha cannot be read"
        rewrite_commit "$commit_object" > "$rewritten_object" \
            || publish_failed transient "rewriting the run commit $old_sha failed"
        new_sha="$(scratch_git hash-object -t commit -w "$rewritten_object" 2> "$git_err")" \
            || publish_invalid "the run commit $old_sha cannot be rewritten: $(last_error)"
        printf '%s %s\n' "$old_sha" "$new_sha" >> "$commit_map"
        subject="$(scratch_git show -s --format=%s "$new_sha" | tr -d '\r')"
        printf 'FOUNDRY_RUN_COMMIT %s %s\n' "$new_sha" "$subject" >> "$run_commit_lines"
    done < "$run_commit_list"
    new_tip="$(map_sha "$tip")"
    [ -n "$new_tip" ] || publish_failed transient "the rewritten run tip is missing"
    echo "Rewrote $(grep -c . "$run_commit_list") run commit(s); new tip $new_tip" >&2

    # --- Push ----------------------------------------------------------------------------------------
    remote_head="$(scratch_git rev-parse --verify --quiet "refs/foundry/remote-branch^{commit}" || true)"

    # Already on the branch (a retried publish whose result was lost): nothing to push.
    if [ -n "$remote_head" ] && scratch_git merge-base --is-ancestor "$new_tip" "$remote_head"; then
        echo "The run is already on $run_branch" >&2
        publish_published "$remote_head"
    fi

    if [ "$tip_kind" = rebased ]; then
        if [ "$remote_head" != "$base_branch" ]; then
            echo "$run_branch moved since Prepare; a rebased run is never merged" >&2
            publish_diverged "$new_tip"
        fi
        if push_ref "$new_tip" "$run_branch" "$base_branch"; then
            publish_published "$new_tip"
        fi
        if grep -q '\[remote rejected\]' "$git_err"; then
            publish_diverged "$new_tip" force-push-protected
        fi
        if grep -q '\[rejected\]' "$git_err"; then
            publish_diverged "$new_tip"
        fi
        fail_git "pushing $run_branch failed"
    fi

    # A forward tip. An absent remote branch (a Fresh run, or a branch deleted during the run) is
    # created; a branch still at the run's base (or at any other ancestor of the tip) is fast-forwarded.
    if [ -z "$remote_head" ] || scratch_git merge-base --is-ancestor "$remote_head" "$new_tip"; then
        push_ref "$new_tip" "$run_branch" || fail_push "pushing $run_branch failed"
        publish_published "$new_tip"
    fi

    # The remote branch moved since Prepare (someone pushed during the run): merge it with the run.
    echo "$run_branch moved since Prepare; merging it with the run" >&2
    if merge_tree="$(scratch_git merge-tree --write-tree --no-messages "$new_tip" "$remote_head" 2> "$git_err")"; then
        merge_tree="$(printf '%s\n' "$merge_tree" | sed -n 1p)"
        merge_sha="$(GIT_AUTHOR_NAME="$AUTHOR_NAME" GIT_AUTHOR_EMAIL="$AUTHOR_EMAIL" \
            GIT_COMMITTER_NAME="$AUTHOR_NAME" GIT_COMMITTER_EMAIL="$AUTHOR_EMAIL" \
            scratch_git commit-tree "$merge_tree" -p "$new_tip" -p "$remote_head" \
                -m "Merge remote-tracking branch 'origin/$run_branch' into $run_branch" 2> "$git_err")" \
            || publish_failed transient "creating the merge commit failed: $(last_error)"
        push_ref "$merge_sha" "$run_branch" || fail_push "pushing $run_branch failed"
        publish_published "$merge_sha"
    fi
    echo "The merge conflicts" >&2
    publish_diverged "$new_tip"
}

case "${1:-}" in
    prepare) prepare ;;
    publish) publish ;;
    *)
        echo "usage: publisher.sh prepare|publish" >&2
        exit 64
        ;;
esac
