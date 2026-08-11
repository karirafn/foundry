#!/bin/bash
set -euo pipefail

# ---------------------------------------------------------------------------
# make_clone_url <clone_url>
#
# Converts a plain HTTPS clone URL into an authenticated URL using the oauth2
# username scheme understood by both GitHub and GitLab:
#   https://host/path[.git] → https://oauth2@host/path.git
#
# The token is NOT embedded in the URL. GIT_ASKPASS delivers it at clone time.
# ---------------------------------------------------------------------------
make_clone_url() {
    local url="$1"
    if [[ "$url" != https://* ]]; then
        echo "ERROR: make_clone_url requires an https:// URL; got: $url" >&2
        return 1
    fi
    # Strip the scheme, insert oauth2@ user, ensure .git suffix
    local without_scheme="${url#https://}"
    local with_user="https://oauth2@${without_scheme}"
    if [[ "$with_user" != *.git ]]; then
        with_user="${with_user}.git"
    fi
    printf '%s' "$with_user"
}

# ---------------------------------------------------------------------------
# make_askpass_script
#
# Returns a minimal sh script string suitable for use as GIT_ASKPASS.
# When git invokes the script for the password, it prints $GIT_PAT.
# Using printf avoids a trailing newline that some git versions misread.
# ---------------------------------------------------------------------------
make_askpass_script() {
    printf '%s' 'printf "%s" "$GIT_PAT"'
}

# ---------------------------------------------------------------------------
# fail_bootstrap <stage> <detail>
#
# Prints the bootstrap-failure sentinel line to stderr so Foundry's log parser
# can classify the failure.  The sentinel format is the single source of truth:
#   FOUNDRY_BOOTSTRAP_FAILED stage=<stage> detail=<detail>
#
# <detail> is collapsed to a single line (newlines replaced with spaces) so the
# parser's line-oriented scan always finds the complete sentinel on one line.
# ---------------------------------------------------------------------------
fail_bootstrap() {
    local stage="$1"
    local detail
    detail="$(printf '%s' "${2:-}" | tr '\n' ' ')"
    printf 'FOUNDRY_BOOTSTRAP_FAILED stage=%s detail=%s\n' "$stage" "$detail" >&2
}

# ---------------------------------------------------------------------------
# start_rootless_dockerd
#
# Starts rootless dockerd in the background and polls until its socket is ready.
# Exports DOCKER_HOST so subsequent commands (e.g. Testcontainers) can reach it.
# Is a no-op when dockerd-rootless.sh is not on PATH (non-Docker image builds).
#
# Tunable via env vars (defaults chosen for typical container cold-start):
#   DOCKER_RETRY_COUNT — number of readiness poll attempts  (default: 30)
#   DOCKER_RETRY_SLEEP — seconds between attempts           (default: 1)
# ---------------------------------------------------------------------------
start_rootless_dockerd() {
    # Unconditionally own DOCKER_HOST so no inherited ambient value survives any
    # exit path (success, no-dockerd, or daemon failure/degraded mode).
    # On success the function re-exports it below; on all other paths it stays unset.
    unset DOCKER_HOST

    if ! command -v dockerd-rootless.sh > /dev/null 2>&1; then
        return 0
    fi

    local uid
    uid="$(id -u)"

    local retry_count="${DOCKER_RETRY_COUNT:-30}"
    # Validate retry_count: must be a non-negative integer no greater than 300
    [[ "$retry_count" =~ ^[0-9]+$ ]] && [ "$retry_count" -le 300 ] || retry_count=30

    local retry_sleep="${DOCKER_RETRY_SLEEP:-1}"
    # Validate retry_sleep: must be a non-negative integer no greater than 60
    [[ "$retry_sleep" =~ ^[0-9]+$ ]] && [ "$retry_sleep" -le 60 ] || retry_sleep=1

    # Derive the home directory from the OS passwd database rather than $HOME so that
    # neither XDG_RUNTIME_DIR nor HOME from the environment can redirect the runtime dir.
    # getent is OS-sourced and cannot be influenced by dispatcher-controlled env vars.
    # /run is root-owned; the unprivileged node user (uid 1000) cannot create dirs there,
    # so the runtime dir lives under the passwd-sourced home where node has write access.
    local _runtime_home
    _runtime_home="$(getent passwd "$(id -u)" | cut -d: -f6)"
    export XDG_RUNTIME_DIR="${_runtime_home}/.runtime"
    mkdir -p "$XDG_RUNTIME_DIR"
    chmod 700 "$XDG_RUNTIME_DIR"

    # Derive socket path from XDG_RUNTIME_DIR so it matches the actual runtime dir in use
    local socket="unix://${XDG_RUNTIME_DIR}/docker.sock"

    # Write daemon output into the runtime dir (owned by node) to avoid /tmp symlink attacks
    local daemon_log="${XDG_RUNTIME_DIR}/dockerd-rootless.log"

    echo "Starting rootless dockerd (uid=${uid}, socket=${socket})..." >&2

    # Launch daemon in background; capture PID so we can detect early exits
    dockerd-rootless.sh > "$daemon_log" 2>&1 &
    local daemon_pid=$!

    local attempt=0
    while [ "$attempt" -lt "$retry_count" ]; do
        # Poll daemon readiness — check the API, not just socket-file existence
        if docker -H "$socket" version > /dev/null 2>&1; then
            export DOCKER_HOST="$socket"
            # DOCKER_HOST is a unix:// URI. Testcontainers .NET (ResourceReaper / Ryuk) derives
            # the Docker socket bind-mount path from the scheme — when the scheme is "unix" it
            # extracts the absolute path from the URI, so no TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE
            # is required. Ryuk therefore mounts the correct rootless-socket path automatically.
            # Source: UnixSocketMount in testcontainers-dotnet/Containers/ResourceReaper.cs
            echo "Rootless dockerd is ready (attempt $((attempt + 1))/${retry_count})" >&2
            return 0
        fi

        # Detect daemon crash before sleeping
        if ! kill -0 "$daemon_pid" 2>/dev/null; then
            echo "ERROR: dockerd-rootless.sh (pid ${daemon_pid}) exited unexpectedly. Log:" >&2
            cat "$daemon_log" >&2
            return 1
        fi

        attempt=$((attempt + 1))
        sleep "$retry_sleep"
    done

    echo "ERROR: Timed out waiting for rootless dockerd after ${retry_count} attempts (${retry_count}s). Log:" >&2
    cat "$daemon_log" >&2
    return 1
}

# Required environment variables
# Valid auth: EITHER ANTHROPIC_API_KEY (API-key mode) OR a credential file at
# $CLAUDE_CONFIG_DIR/.credentials.json (OAuth volume mode, written by claude /login).
_cred_file="${CLAUDE_CONFIG_DIR:-}/.credentials.json"
if [[ -z "${ANTHROPIC_API_KEY:-}" ]] && [[ -z "${CLAUDE_CONFIG_DIR:-}" || ! -f "$_cred_file" ]]; then
    echo "Authentication required: set ANTHROPIC_API_KEY (API-key mode) or mount a credentials file at \$CLAUDE_CONFIG_DIR/.credentials.json (OAuth mode)" >&2
    exit 1
fi
unset _cred_file
: "${CLONE_URL:?CLONE_URL is required}"
: "${GIT_PAT:?GIT_PAT is required}"
: "${WORKER_PROMPT:?WORKER_PROMPT is required}"
: "${SYSTEM_PROMPT:?SYSTEM_PROMPT is required}"
: "${ISSUE_NUMBER:?ISSUE_NUMBER is required}"

if [[ -n "${CLAUDE_SETTINGS_JSON:-}" ]]; then
    if [[ ! -w ~/.claude ]]; then
        echo "ERROR: ~/.claude is not writable by $(whoami). Rebuild the worker image: docker build -t foundry-worker:local workers/" >&2
        exit 1
    fi
    # Write via a temp file and atomically replace any pre-existing read-only settings.json.
    # mv -f replaces a 444 file owned by the same user (node owns the dir, so replacement is
    # permitted), fixing both re-run breakage and any concurrent-worker write race (last writer
    # wins with identical content). settings.json is regenerated fresh on every container start.
    settings_tmp="$(mktemp)"
    printf '%s\n' "$CLAUDE_SETTINGS_JSON" > "$settings_tmp"
    chmod 444 "$settings_tmp"
    mv -f "$settings_tmp" ~/.claude/settings.json
fi

# Authenticate the clone via GIT_ASKPASS so the token never appears in the
# clone URL, on a command line, or in git's output.  The clone URL uses the
# oauth2 username (accepted by both GitHub and GitLab); GIT_ASKPASS delivers
# the password ($GIT_PAT) when git prompts for credentials.
CLONE_AUTH_URL="$(make_clone_url "$CLONE_URL")"

ASKPASS_FILE="$(mktemp)"
# Ensure the temp file is removed on every exit path (including clone failure)
trap 'rm -f "${ASKPASS_FILE:-}"' EXIT
printf '%s\n' "#!/bin/sh" "$(make_askpass_script)" > "$ASKPASS_FILE"
chmod 700 "$ASKPASS_FILE"

# Arm the bootstrap ERR trap before the first bootstrap step.
# Each step updates BOOTSTRAP_STAGE so the trap reports the correct stage.
# The trap is disarmed before start_rootless_dockerd — failures there are
# out of scope for bootstrap classification (see issue #221).
BOOTSTRAP_STAGE="clone"
trap 'fail_bootstrap "$BOOTSTRAP_STAGE" "bootstrap failed at stage $BOOTSTRAP_STAGE"' ERR

# GIT_ASKPASS is scoped to this one command — not exported — so push falls
# through to the gh/glab credential helpers set up below.
GIT_ASKPASS="$ASKPASS_FILE" git clone "$CLONE_AUTH_URL" /workspace

rm -f "$ASKPASS_FILE"
unset ASKPASS_FILE
trap - EXIT

# GIT_PAT is no longer needed after clone — unset it before invoking claude
# so the token is not visible in the container's environment during the run.
# Push auth uses GH_TOKEN / GITLAB_TOKEN via the gh/glab credential helpers.
unset GIT_PAT

git -C /workspace remote set-url origin "$CLONE_URL"

cd /workspace

BOOTSTRAP_STAGE="auth"
if [[ -n "${GH_TOKEN:-}" ]]; then
    GH_HOST="${CLONE_URL#https://}"
    GH_HOST="${GH_HOST%%/*}"
    if [[ -z "$GH_HOST" ]]; then
        echo "WARNING: could not derive hostname from CLONE_URL; skipping gh auth setup-git" >&2
    elif [[ "$GH_HOST" == *@* ]]; then
        echo "ERROR: derived hostname contains '@' — CLONE_URL may embed credentials in the URL. Aborting gh auth setup-git." >&2
        fail_bootstrap "auth" "derived hostname contains @ — CLONE_URL may embed credentials"
        exit 1
    elif command -v gh > /dev/null 2>&1; then
        gh auth setup-git --hostname "$GH_HOST" --force
    else
        echo "WARNING: GH_TOKEN is set but the gh CLI is not present in this image." >&2
        echo "  git push and gh pr create will not authenticate." >&2
        echo "  Rebuild the worker image with INSTALL_GH=true: docker build --build-arg INSTALL_GH=true -t foundry-worker:local workers/" >&2
    fi
fi

if [[ -n "${GITLAB_TOKEN:-}" ]]; then
    GL_HOST="${CLONE_URL#https://}"
    GL_HOST="${GL_HOST%%/*}"
    if [[ -z "$GL_HOST" ]]; then
        echo "WARNING: could not derive hostname from CLONE_URL; skipping glab credential helper setup" >&2
    elif [[ "$GL_HOST" == *@* ]]; then
        echo "ERROR: derived hostname contains '@' — CLONE_URL may embed credentials in the URL. Aborting glab credential helper setup." >&2
        fail_bootstrap "auth" "derived hostname contains @ — CLONE_URL may embed credentials"
        exit 1
    elif command -v glab > /dev/null 2>&1; then
        export GITLAB_HOST="https://${GL_HOST}"
        git config credential."https://${GL_HOST}".helper "!glab auth git-credential"
    else
        echo "WARNING: GITLAB_TOKEN is set but the glab CLI is not present in this image." >&2
        echo "  git push and glab mr create will not authenticate." >&2
        echo "  Rebuild the worker image with INSTALL_GLAB=true: docker build --build-arg INSTALL_GLAB=true -t foundry-worker:local workers/" >&2
    fi
fi

BOOTSTRAP_STAGE="branch"
if [[ -n "${BRANCH_NAME:-}" ]]; then
    if [[ ! "$BRANCH_NAME" =~ ^[a-zA-Z0-9_/.-]+$ ]]; then
        echo "ERROR: BRANCH_NAME contains invalid characters: $BRANCH_NAME" >&2
        fail_bootstrap "branch" "invalid BRANCH_NAME characters"
        exit 1
    fi
    git switch -- "$BRANCH_NAME" || git switch -c "$BRANCH_NAME"
fi

# Disarm the bootstrap ERR trap — failures beyond this point (rootless-dockerd
# startup, claude invocation) are out of scope for bootstrap classification.
trap - ERR

if start_rootless_dockerd; then
    :
else
    echo "WARNING: rootless dockerd unavailable; continuing in degraded mode (unit tests only, no Docker daemon). DOCKER_HOST left unset; integration tests will execute where a daemon exists (CI / native-Linux host)." >&2
    echo "FOUNDRY_DOCKER_UNAVAILABLE detail=rootless dockerd failed to start" >&2
fi

# ---------------------------------------------------------------------------
# is_transient_api_error <json_output>
#
# Returns 0 (true) when the captured claude JSON output classifies as a
# transient API error, 1 (false) otherwise.
#
# Mirrors ContainerOutputParser.IsTransientApiError in C# — the detection
# criteria MUST stay in sync between here and that class.
#
# Classification inspects only the LAST JSON line (the line whose first
# non-space character is '{', walking backward through the output), matching
# ExtractLastJsonLine in C#.  Earlier lines that happen to contain 5xx
# statuses or error phrases do NOT trigger a resume.
#
# Transient when EITHER:
#   - api_error_status is in the 500–599 range (numeric or string), OR
#   - is_error is true AND the last JSON line contains one of these phrases:
#       "API Error: Connection closed mid-response"
#       "API Error: 529 Overloaded"
#
# Note: the phrase check runs against the whole last JSON line rather than
# just the "result" field value (a minor widening vs C# which extracts only
# the result string).  This is benign: both phrases are API Error messages
# that only appear inside the result field in practice.
# ---------------------------------------------------------------------------
is_transient_api_error() {
    local output="$1"

    # Extract the last line whose first non-space character is '{' — mirrors
    # ExtractLastJsonLine in ContainerOutputParser.cs.
    local last_json_line
    last_json_line="$(printf '%s\n' "$output" | grep -E '^[[:space:]]*\{' | tail -n 1)"

    # No JSON object line found — cannot be a transient API error.
    if [[ -z "$last_json_line" ]]; then
        return 1
    fi

    # Extract api_error_status value from the last JSON line (numeric or quoted numeric)
    local status_raw
    status_raw="$(printf '%s' "$last_json_line" | grep -o '"api_error_status":[[:space:]]*[0-9"]*' | grep -o '[0-9]\{3,\}' | head -1)"

    if [[ -n "$status_raw" ]] && [ "$status_raw" -ge 500 ] 2>/dev/null && [ "$status_raw" -le 599 ] 2>/dev/null; then
        return 0
    fi

    # Check is_error flag (true) combined with known transient phrases
    if printf '%s' "$last_json_line" | grep -q '"is_error":[[:space:]]*true'; then
        if printf '%s' "$last_json_line" | grep -qF 'API Error: Connection closed mid-response'; then
            return 0
        fi
        if printf '%s' "$last_json_line" | grep -qF 'API Error: 529 Overloaded'; then
            return 0
        fi
    fi

    return 1
}

# ---------------------------------------------------------------------------
# Invoke claude in headless/JSON mode.  Capture stdout (the single JSON
# result line) while letting stderr stream through live so logs are visible.
# set -e is toggled off around the capture so we can inspect the exit status.
#
# If the first run classifies as a transient API error (mirrors
# ContainerOutputParser.IsTransientApiError), resume the interrupted session
# ONCE via "claude -c" before giving up.  The resume's JSON result line
# becomes the last line of output — if it also fails transiently, Foundry's
# outer retry bound (#368) still applies.
# ---------------------------------------------------------------------------
set +e
first_output="$(claude -p "$WORKER_PROMPT" \
    --append-system-prompt "$SYSTEM_PROMPT" \
    --dangerously-skip-permissions \
    --output-format json \
    --max-turns 200)"
first_status=$?
set -e

[[ -n "$first_output" ]] && printf '%s\n' "$first_output"

if is_transient_api_error "$first_output"; then
    echo "Transient API error detected — resuming session once via claude -c" >&2
    # Resume the most recent conversation in cwd.  --append-system-prompt is
    # intentionally omitted: -c restores the original session (system prompt
    # included) and re-appending would duplicate it.
    set +e
    resume_output="$(claude -c -p "$WORKER_PROMPT" \
        --dangerously-skip-permissions \
        --output-format json \
        --max-turns 200)"
    resume_status=$?
    set -e
    [[ -n "$resume_output" ]] && printf '%s\n' "$resume_output"
    exit "$resume_status"
fi

exit "$first_status"
