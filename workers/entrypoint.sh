#!/bin/bash
set -euo pipefail

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
if [[ -z "${ANTHROPIC_API_KEY:-}" ]] && [[ -z "${CLAUDE_CODE_OAUTH_TOKEN:-}" ]]; then
    echo "Either ANTHROPIC_API_KEY or CLAUDE_CODE_OAUTH_TOKEN is required" >&2
    exit 1
fi
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
    printf '%s\n' "$CLAUDE_SETTINGS_JSON" > ~/.claude/settings.json
    chmod 444 ~/.claude/settings.json
fi

# Transform https://github.com/owner/repo -> https://<PAT>@github.com/owner/repo.git
AUTHENTICATED_URL="${CLONE_URL/#https:\/\//https:\/\/${GIT_PAT}@}"
if [[ "$AUTHENTICATED_URL" != *.git ]]; then
    AUTHENTICATED_URL="${AUTHENTICATED_URL}.git"
fi

git clone "$AUTHENTICATED_URL" /workspace
git -C /workspace remote set-url origin "$CLONE_URL"

cd /workspace

if [[ -n "${GH_TOKEN:-}" ]]; then
    GH_HOST="${CLONE_URL#https://}"
    GH_HOST="${GH_HOST%%/*}"
    if [[ -z "$GH_HOST" ]]; then
        echo "WARNING: could not derive hostname from CLONE_URL; skipping gh auth setup-git" >&2
    elif [[ "$GH_HOST" == *@* ]]; then
        echo "ERROR: derived hostname contains '@' — CLONE_URL may embed credentials in the URL. Aborting gh auth setup-git." >&2
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

if [[ -n "${BRANCH_NAME:-}" ]]; then
    if [[ ! "$BRANCH_NAME" =~ ^[a-zA-Z0-9_/.-]+$ ]]; then
        echo "ERROR: BRANCH_NAME contains invalid characters: $BRANCH_NAME" >&2
        exit 1
    fi
    git switch -- "$BRANCH_NAME" || git switch -c "$BRANCH_NAME"
fi

start_rootless_dockerd

claude -p "$WORKER_PROMPT" \
    --append-system-prompt "$SYSTEM_PROMPT" \
    --dangerously-skip-permissions \
    --output-format json \
    --max-turns 200
