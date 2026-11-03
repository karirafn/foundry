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
# merge_host_hooks <settings_json> <host_settings_file> <exclusions_json>
#
# Prints <settings_json> with the `hooks` object of the operator's host Claude
# Code settings file merged in.  Only `hooks` is imported — every other host key
# (permissions, env, statusLine, ...) is ignored.  Every hook entry whose
# `command` exactly matches an entry of the <exclusions_json> array is dropped;
# matcher groups and events left empty are removed.  Host groups are appended
# after any groups the generated settings already carry for the same event.
#
# Logs "Host hooks applied: N (excluded: M)", each applied command and each
# exclusion that matched no hook to stderr.
#
# Fails closed: a missing, unreadable or invalid file (not a JSON object, or a
# `hooks` value that is not an object of matcher-group arrays) emits the
# bootstrap-failure sentinel naming the file and returns non-zero — hooks are
# guardrails, so silently dropping them would produce unchecked commits.
# ---------------------------------------------------------------------------
merge_host_hooks() {
    local settings_json="$1"
    local host_file="$2"
    local exclusions_json="${3:-[]}"

    if ! command -v jq > /dev/null 2>&1; then
        fail_bootstrap "host-settings" "jq is not installed; cannot merge hooks from host settings file $host_file"
        return 1
    fi

    if [[ ! -f "$host_file" || ! -r "$host_file" ]]; then
        echo "ERROR: host settings file $host_file is missing or unreadable." >&2
        fail_bootstrap "host-settings" "host settings file $host_file is missing or unreadable"
        return 1
    fi

    local host_json
    if ! host_json="$(jq -cs '
            if length == 1
               and (.[0] | type) == "object"
               and ((.[0].hooks // {}) | type) == "object"
               and all((.[0].hooks // {})[]; type == "array" and all(.[]; type == "object" and ((.hooks // []) | type) == "array"))
            then .[0]
            else error("unexpected shape")
            end' "$host_file" 2> /dev/null)"; then
        echo "ERROR: host settings file $host_file is not valid JSON or has an unexpected hooks shape." >&2
        fail_bootstrap "host-settings" "host settings file $host_file is not valid JSON"
        return 1
    fi

    if ! printf '%s' "$exclusions_json" | jq -e 'type == "array" and all(.[]; type == "string")' > /dev/null 2>&1; then
        fail_bootstrap "host-settings" "hook exclusion list is not a JSON array of strings; cannot merge host settings file $host_file"
        return 1
    fi

    # errexit is suspended inside a function whose caller tests its status, so every
    # jq failure below is checked explicitly.
    local merge_result
    if ! merge_result="$(jq -cn \
        --argjson base "$settings_json" \
        --argjson host "$host_json" \
        --argjson excluded "$exclusions_json" '
        def excluded($command): $command != null and any($excluded[]; . == $command);
        def hook_label: .command // ("<" + (.type // "hook") + " hook>");

        ($host.hooks // {}) as $hooks
        | [$hooks[][] | (.hooks // [])[]] as $all
        | ($hooks
            | with_entries(
                .value |= [.[] | .hooks = [(.hooks // [])[] | select(excluded(.command) | not)] | select(.hooks | length > 0)]
              )
            | with_entries(select(.value | length > 0))) as $kept
        | [$kept[][] | .hooks[]] as $applied
        | {
            settings: (
              if ($kept | length) == 0 then $base
              else $base | .hooks = (reduce ($kept | to_entries[]) as $e (.hooks // {};
                     .[$e.key] = ((.[$e.key] // []) + $e.value)))
              end),
            applied: [$applied[] | hook_label],
            excluded_count: ([$all[] | select(excluded(.command))] | length),
            unmatched: [$excluded[] | . as $x | select(any($all[]; .command == $x) | not)]
          }')"; then
        fail_bootstrap "host-settings" "could not merge hooks from host settings file $host_file"
        return 1
    fi

    local applied_count excluded_count
    applied_count="$(printf '%s' "$merge_result" | jq '.applied | length')"
    excluded_count="$(printf '%s' "$merge_result" | jq '.excluded_count')"

    echo "Host hooks applied: ${applied_count} (excluded: ${excluded_count})" >&2
    printf '%s' "$merge_result" | jq -r '.applied[] | "  applied: " + .' >&2
    printf '%s' "$merge_result" | jq -r '.unmatched[] | "  exclusion matched no hook: " + .' >&2

    printf '%s' "$merge_result" | jq -c '.settings'
}

# ---------------------------------------------------------------------------
# start_rootless_dockerd
#
# Starts rootless dockerd in the background and polls until its socket is ready.
# Exports DOCKER_HOST so subsequent commands (e.g. Testcontainers) can reach it, and
# TESTCONTAINERS_HOST_OVERRIDE=127.0.0.1 so they connect to published ports there.
# Is a no-op when dockerd-rootless.sh is not on PATH (non-Docker image builds).
#
# Tunable via env vars (defaults chosen for typical container cold-start):
#   DOCKER_RETRY_COUNT — number of readiness poll attempts  (default: 30)
#   DOCKER_RETRY_SLEEP — seconds between attempts           (default: 1)
# ---------------------------------------------------------------------------
start_rootless_dockerd() {
    # Unconditionally own DOCKER_HOST and every TESTCONTAINERS_* variable, swept by prefix
    # so newly introduced Testcontainers settings are covered too, and no inherited
    # ambient value survives any exit path (success, no-dockerd, or daemon
    # failure/degraded mode). On success the function re-exports what it needs below; on
    # all other paths they stay unset. The grep's no-match status is absorbed with
    # `|| true` so the sweep is safe under set -e / pipefail.
    unset DOCKER_HOST
    local _tc_name
    while IFS= read -r _tc_name; do
        unset "$_tc_name"
    done < <(compgen -e | grep '^TESTCONTAINERS_' || true)

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
            # Testcontainers inside a container resolves the host to the default bridge
            # gateway (172.17.0.1), which under rootless dockerd lives in rootlesskit's network
            # namespace; published ports are forwarded only to the worker's own localhost, so
            # the gateway refuses connections (breaking Ryuk and every mapped-port client).
            # IPv4 loopback is pinned because the name localhost may resolve to ::1 first,
            # and clients without address fallback then fail against an IPv4-only forwarder.
            export TESTCONTAINERS_HOST_OVERRIDE=127.0.0.1
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

# Guard: SYSTEM_PROMPT must not exceed the Linux kernel MAX_ARG_STRLEN limit.
# A single argv/env string above this ceiling makes execve fail with E2BIG,
# producing an opaque "Argument list too long" error before claude starts.
# The C# builder caps at 120 000 bytes (design ceiling); this shell guard is
# the last-line-of-defence at the true kernel limit so any drift or caller
# bypass is caught with an explicit diagnostic before any clone/auth work.
readonly MAX_SYSTEM_PROMPT_BYTES=131071
sys_prompt_bytes=$(printf '%s' "$SYSTEM_PROMPT" | wc -c)
if [[ "$sys_prompt_bytes" -ge "$MAX_SYSTEM_PROMPT_BYTES" ]]; then
    echo "ERROR: SYSTEM_PROMPT exceeds the kernel MAX_ARG_STRLEN ceiling of ${MAX_SYSTEM_PROMPT_BYTES} bytes (actual: ${sys_prompt_bytes} bytes). Reduce the prompt before dispatching." >&2
    exit 1
fi
unset sys_prompt_bytes

if [[ -n "${CLAUDE_SETTINGS_JSON:-}" || -n "${FOUNDRY_HOST_SETTINGS_FILE:-}" ]]; then
    if [[ ! -w ~/.claude ]]; then
        echo "ERROR: ~/.claude is not writable by $(whoami). Rebuild the worker image: docker build -t foundry-worker:local workers/" >&2
        exit 1
    fi
    settings_json="${CLAUDE_SETTINGS_JSON:-}"
    [[ -n "$settings_json" ]] || settings_json='{}'
    # The host settings file is mounted read-only at a staging path (never ~/.claude/settings.json);
    # only its hooks are merged in, minus the operator's exclusion list.
    if [[ -n "${FOUNDRY_HOST_SETTINGS_FILE:-}" ]]; then
        settings_json="$(merge_host_hooks "$settings_json" "$FOUNDRY_HOST_SETTINGS_FILE" "${FOUNDRY_HOST_HOOK_EXCLUSIONS:-[]}")" || exit 1
    fi
    # Write via a temp file and atomically replace any pre-existing read-only settings.json.
    # mv -f replaces a 444 file owned by the same user (node owns the dir, so replacement is
    # permitted), fixing both re-run breakage and any concurrent-worker write race (last writer
    # wins with identical content). settings.json is regenerated fresh on every container start.
    settings_tmp="$(mktemp)"
    printf '%s\n' "$settings_json" > "$settings_tmp"
    chmod 444 "$settings_tmp"
    mv -f "$settings_tmp" ~/.claude/settings.json
    unset settings_json settings_tmp
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
        echo "ERROR: GH_TOKEN is set but the gh CLI is not present in this image." >&2
        fail_bootstrap "auth" "gh CLI not present but GH_TOKEN is set — git push and gh pr create cannot authenticate"
        exit 1
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
        echo "ERROR: GITLAB_TOKEN is set but the glab CLI is not present in this image." >&2
        fail_bootstrap "auth" "glab CLI not present but GITLAB_TOKEN is set — git push and glab mr create cannot authenticate"
        exit 1
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
# is_transient_api_error <stream_json_output>
#
# Returns 0 (true) when the captured claude stream-json output classifies as a
# transient API error, 1 (false) otherwise.
#
# Mirrors ContainerOutputParser.IsTransientApiError in C# — the detection
# criteria MUST stay in sync between here and that class.
#
# Classification inspects only the LAST result event — the last JSON line
# (first non-space character '{') carrying "type":"result" — matching
# ExtractLastResultLine in C#.  Every other stream-json event (assistant,
# user, system, ...) and earlier lines that happen to contain 5xx statuses or
# error phrases do NOT trigger a resume.
#
# Transient when EITHER:
#   - api_error_status is in the 500–599 range (numeric or string), OR
#   - is_error is true AND the result line contains one of these phrases:
#       "API Error: Connection closed mid-response"
#       "API Error: 529 Overloaded"
#
# Note: the phrase check runs against the whole result line rather than just
# the "result" field value (a minor widening vs C# which extracts only the
# result string).  This is benign: both phrases are API Error messages that
# only appear inside the result field in practice.
# ---------------------------------------------------------------------------
is_transient_api_error() {
    local output="$1"

    # Extract the last result event line — mirrors ExtractLastResultLine in
    # ContainerOutputParser.cs.  The "type" key is not necessarily first in
    # the object, so match it anywhere on a line that starts with '{'.
    local last_json_line
    last_json_line="$(printf '%s\n' "$output" | grep -E '^[[:space:]]*\{' | grep -E '"type":[[:space:]]*"result"' | tail -n 1)"

    # No result event found — cannot be a transient API error.
    if [[ -z "$last_json_line" ]]; then
        return 1
    fi

    # Extract api_error_status value from the result line (numeric or quoted numeric)
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
# Invoke claude in headless stream-json mode.  stream-json emits one event per
# line as the run progresses (the final line is the "result" event), so stdout
# is piped through tee: each line reaches the container log as it is produced
# while a copy is kept for transient-error classification.  stderr streams
# through live.  --verbose is mandatory: the CLI rejects stream-json with
# --print unless --verbose is also set.
# set -e is toggled off around the pipeline so we can inspect claude's exit
# status (PIPESTATUS[0], not tee's).
#
# If the first run classifies as a transient API error (mirrors
# ContainerOutputParser.IsTransientApiError), resume the interrupted session
# ONCE via "claude -c" with the same output flags before giving up.  The
# resume's result event becomes the last result in the log — if it also fails
# transiently, Foundry's outer retry bound (#368) still applies.
# ---------------------------------------------------------------------------
CLAUDE_OUTPUT_FILE="$(mktemp)"
trap 'rm -f "${CLAUDE_OUTPUT_FILE:-}"' EXIT

set +e
claude -p "$WORKER_PROMPT" \
    --append-system-prompt "$SYSTEM_PROMPT" \
    --dangerously-skip-permissions \
    --output-format stream-json \
    --verbose \
    --max-turns 200 | tee "$CLAUDE_OUTPUT_FILE"
first_status=${PIPESTATUS[0]}
set -e

if is_transient_api_error "$(cat "$CLAUDE_OUTPUT_FILE")"; then
    echo "Transient API error detected — resuming session once via claude -c" >&2
    # Resume the most recent conversation in cwd.  --append-system-prompt is
    # intentionally omitted: -c restores the original session (system prompt
    # included) and re-appending would duplicate it.
    set +e
    claude -c -p "$WORKER_PROMPT" \
        --dangerously-skip-permissions \
        --output-format stream-json \
        --verbose \
        --max-turns 200 | tee "$CLAUDE_OUTPUT_FILE"
    resume_status=${PIPESTATUS[0]}
    set -e
    exit "$resume_status"
fi

exit "$first_status"
