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
# prepare_csharp_lsp <workspace_root>
#
# Decides whether claude loads the baked csharp-lsp plugin and records the
# decision in the CSHARP_LSP_PLUGIN_ARGS array, which both claude invocations
# (the initial -p run and the transient-error -c resume) expand verbatim.
#
# csharp-ls snapshots the workspace when it starts and never picks up a later
# restore, so loading it against an unrestored repo yields silently partial
# results.  This therefore restores first and fails closed to "no LSP" (the
# agent falls back to grep) on any problem; it never fails the bootstrap.
#
# Restore targets mirror csharp-ls's own discovery: every *.sln / *.slnx found
# recursively (excluding node_modules), else every *.csproj / *.fsproj.  The
# superset is restored so whichever solution csharp-ls picks is covered.  All
# restores share one overall CSHARP_LSP_RESTORE_TIMEOUT_SECONDS budget.
#
# Prints exactly one "C# LSP: enabled" or "C# LSP: disabled (<reason>)" line
# to stderr; restore output also goes to stderr so stdout stays stream-json.
# ---------------------------------------------------------------------------
prepare_csharp_lsp() {
    local root="$1"
    CSHARP_LSP_PLUGIN_ARGS=()

    if ! command -v csharp-ls > /dev/null 2>&1; then
        echo "C# LSP: disabled (csharp-ls not on PATH)" >&2
        return 0
    fi
    if ! command -v dotnet > /dev/null 2>&1; then
        echo "C# LSP: disabled (dotnet not on PATH)" >&2
        return 0
    fi

    local -a targets=()
    mapfile -d '' -t targets < <(find "$root" -name node_modules -prune -o -type f \( -name '*.sln' -o -name '*.slnx' \) -print0 | sort -z)
    if (( ${#targets[@]} == 0 )); then
        mapfile -d '' -t targets < <(find "$root" -name node_modules -prune -o -type f \( -name '*.csproj' -o -name '*.fsproj' \) -print0 | sort -z)
    fi
    if (( ${#targets[@]} == 0 )); then
        echo "C# LSP: disabled (no solution or project files)" >&2
        return 0
    fi

    local deadline=$(( SECONDS + CSHARP_LSP_RESTORE_TIMEOUT_SECONDS ))
    local target remaining rc
    for target in "${targets[@]}"; do
        remaining=$(( deadline - SECONDS ))
        if (( remaining <= 0 )); then
            echo "C# LSP: disabled (restore exceeded ${CSHARP_LSP_RESTORE_TIMEOUT_SECONDS}s)" >&2
            return 0
        fi
        echo "C# LSP: restoring ${target#"$root"/}" >&2
        rc=0
        timeout --kill-after=10 "$remaining" dotnet restore "$target" >&2 || rc=$?
        if (( rc == 124 || rc == 137 )); then
            echo "C# LSP: disabled (restore exceeded ${CSHARP_LSP_RESTORE_TIMEOUT_SECONDS}s)" >&2
            return 0
        elif (( rc != 0 )); then
            echo "C# LSP: disabled (dotnet restore failed for ${target#"$root"/} with exit code ${rc})" >&2
            return 0
        fi
    done

    CSHARP_LSP_PLUGIN_ARGS=(--plugin-dir "$CSHARP_LSP_PLUGIN_DIR")
    echo "C# LSP: enabled" >&2
}

# ---------------------------------------------------------------------------
# prepare_role_plugins <roles_dir> <role>
#
# Resolves the run's Worker Role (ADR 0095) to the plugin directories claude
# loads, recorded in the ROLE_PLUGIN_ARGS array that both claude invocations
# (the initial -p run and the transient-error -c resume) expand verbatim: the
# role's own plugin, then the shared plugin loaded for every role.
#
# Fails closed: a role name that is not a plain lowercase slug, or a role (or
# the shared plugin) with no plugin manifest under <roles_dir>, emits the
# bootstrap-failure sentinel (stage=role) and returns non-zero, so a run never
# starts without the instructions Foundry dispatched it with.
# ---------------------------------------------------------------------------
prepare_role_plugins() {
    local roles_dir="$1"
    local role="$2"
    ROLE_PLUGIN_ARGS=()

    if [[ ! "$role" =~ ^[a-z][a-z0-9-]*$ ]] || [[ "$role" == "shared" ]]; then
        echo "ERROR: WORKER_ROLE '$role' is not a valid worker role name." >&2
        fail_bootstrap "role" "WORKER_ROLE is not a valid worker role name"
        return 1
    fi

    local plugin_dir
    for plugin_dir in "$roles_dir/$role" "$roles_dir/shared"; do
        if [[ ! -f "$plugin_dir/.claude-plugin/plugin.json" ]]; then
            echo "ERROR: worker role plugin $plugin_dir is missing from this image." >&2
            fail_bootstrap "role" "worker role plugin $plugin_dir is missing from this image; rebuild the worker image"
            return 1
        fi
        ROLE_PLUGIN_ARGS+=(--plugin-dir "$plugin_dir")
    done

    echo "Worker role: $role" >&2
}

# ---------------------------------------------------------------------------
# prepare_role_verdict_schema <roles_dir> <role>
#
# Resolves the run's verdict schema (ADR 0094) into the VERDICT_SCHEMA_ARGS
# array that both claude invocations (the initial -p run and the transient-error
# -c resume) expand verbatim: --json-schema with the role's verdict.schema.json,
# or nothing when the role has no schema.
#
# A role schema references shared definitions (e.g. "#/$defs/questions") from
# <roles_dir>/shared/verdict.defs.json; their $defs are merged into the role
# schema (the role's own $defs win on a name clash) so the CLI receives one
# self-contained schema, compacted to a single line.
#
# Fails closed: a schema (or the shared definitions) that is not a JSON object
# emits the bootstrap-failure sentinel (stage=role) and returns non-zero.
# ---------------------------------------------------------------------------
prepare_role_verdict_schema() {
    local roles_dir="$1"
    local role="$2"
    VERDICT_SCHEMA_ARGS=()

    local schema_file="$roles_dir/$role/verdict.schema.json"
    if [[ ! -f "$schema_file" ]]; then
        return 0
    fi

    local defs_file="$roles_dir/shared/verdict.defs.json"
    local defs_json='{}'
    if [[ -f "$defs_file" ]]; then
        if ! defs_json="$(jq -ce 'if type == "object" then . else error("not an object") end' "$defs_file" 2>/dev/null)"; then
            echo "ERROR: $defs_file is not a JSON object." >&2
            fail_bootstrap "role" "shared verdict definitions $defs_file are not a JSON object; rebuild the worker image"
            return 1
        fi
    fi

    local schema
    if ! schema="$(jq -ce --argjson shared "$defs_json" '
            if type != "object" then error("not an object") else . end
            | if ($shared["$defs"] // {}) == {} then .
              else .["$defs"] = (($shared["$defs"] // {}) + (.["$defs"] // {})) end
        ' "$schema_file" 2>/dev/null)"; then
        echo "ERROR: $schema_file is not a JSON object." >&2
        fail_bootstrap "role" "verdict schema $schema_file is not a JSON object; rebuild the worker image"
        return 1
    fi

    VERDICT_SCHEMA_ARGS=(--json-schema "$schema")
    echo "Verdict schema: $role" >&2
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

# ---------------------------------------------------------------------------
# Claude Code's Linux managed-settings path.  Settings here outrank user,
# project, local and --settings values, and their hooks cannot be removed or
# disabled from any other file (ADR 0086).
# ---------------------------------------------------------------------------
readonly MANAGED_SETTINGS_FILE=/etc/claude-code/managed-settings.json

# ---------------------------------------------------------------------------
# render_managed_settings <output_path>
#
# Renders Foundry's worker policy — $CLAUDE_SETTINGS_JSON (deny lists, model) —
# to <output_path>.  The parent directory is made mode 0755 and the file mode 0644:
# world-readable, because Claude Code starts without a managed source it cannot
# read, so a root-only file would silently drop the policy.  When run as root
# (production) both are chowned root:root, so the unprivileged worker can read
# but never write, chmod or replace them.
#
# Fails closed: an empty, missing or non-object $CLAUDE_SETTINGS_JSON emits
# the bootstrap-failure sentinel (stage=managed-settings) and returns non-zero.
# ---------------------------------------------------------------------------
render_managed_settings() {
    local output_path="$1"
    local output_dir
    output_dir="$(dirname "$output_path")"

    local settings_json="${CLAUDE_SETTINGS_JSON:-}"
    if [[ -z "$settings_json" ]]; then
        echo "ERROR: CLAUDE_SETTINGS_JSON is empty or missing; refusing to start a worker without its managed settings." >&2
        fail_bootstrap "managed-settings" "CLAUDE_SETTINGS_JSON is empty or missing; the worker policy cannot be rendered"
        return 1
    fi

    if ! command -v jq > /dev/null 2>&1; then
        fail_bootstrap "managed-settings" "jq is not installed; cannot render $output_path"
        return 1
    fi

    if ! printf '%s' "$settings_json" | jq -e 'type == "object"' > /dev/null 2>&1; then
        echo "ERROR: CLAUDE_SETTINGS_JSON is not a JSON object." >&2
        fail_bootstrap "managed-settings" "CLAUDE_SETTINGS_JSON is not a JSON object; the worker policy cannot be rendered"
        return 1
    fi

    # allowManagedHooksOnly would also block the target repository's own hooks; managed hooks
    # are tamper-proof without it, so it is never carried into the managed file.
    if ! settings_json="$(printf '%s' "$settings_json" | jq 'del(.allowManagedHooksOnly)')"; then
        fail_bootstrap "managed-settings" "could not render $output_path"
        return 1
    fi

    local is_root=false
    [[ "$(id -u)" -eq 0 ]] && is_root=true

    if ! mkdir -p "$output_dir" || ! chmod 0755 "$output_dir" \
        || { $is_root && ! chown 0:0 "$output_dir"; }; then
        fail_bootstrap "managed-settings" "could not create $output_dir"
        return 1
    fi

    # Write via a temp file in the same directory and atomically rename it into place.
    local settings_tmp
    if ! settings_tmp="$(mktemp "${output_dir}/.managed-settings.XXXXXX")"; then
        fail_bootstrap "managed-settings" "could not create a temp file in $output_dir"
        return 1
    fi
    if ! printf '%s\n' "$settings_json" > "$settings_tmp" \
        || ! chmod 0644 "$settings_tmp" \
        || { $is_root && ! chown 0:0 "$settings_tmp"; } \
        || ! mv -f "$settings_tmp" "$output_path"; then
        rm -f "$settings_tmp"
        fail_bootstrap "managed-settings" "could not write $output_path"
        return 1
    fi

    echo "Managed settings rendered to $output_path" >&2
}

# ---------------------------------------------------------------------------
# verify_managed_settings <path>
#
# Non-root invariant: <path> must exist, be owned by uid 0 and carry no group
# or other write bit.  Otherwise emits the bootstrap-failure sentinel
# (stage=managed-settings) and returns non-zero — this catches a container
# started directly as a non-root user (e.g. `--user node`) or from a stale
# image that skipped the root render phase.
# ---------------------------------------------------------------------------
verify_managed_settings() {
    local path="$1"
    local rebuild_hint="rebuild the worker image (docker build -t foundry-worker:local workers/) and start it as root so the entrypoint can render the managed settings"

    local stat_out
    if ! stat_out="$(stat -L -c '%u %a' -- "$path" 2> /dev/null)" || [[ -z "$stat_out" ]]; then
        echo "ERROR: managed settings file $path is missing." >&2
        fail_bootstrap "managed-settings" "managed settings file $path is missing; $rebuild_hint"
        return 1
    fi

    local owner mode
    read -r owner mode <<< "$stat_out"
    if [[ "$owner" != "0" ]]; then
        echo "ERROR: managed settings file $path is owned by uid $owner, not root." >&2
        fail_bootstrap "managed-settings" "managed settings file $path is not owned by root (uid $owner); $rebuild_hint"
        return 1
    fi
    if [[ ! "$mode" =~ ^[0-7]+$ ]] || (( (8#$mode & 8#022) != 0 )); then
        echo "ERROR: managed settings file $path has mode $mode; it must not be group- or other-writable." >&2
        fail_bootstrap "managed-settings" "managed settings file $path is group- or other-writable (mode $mode); $rebuild_hint"
        return 1
    fi
}

# ---------------------------------------------------------------------------
# Root phase: render the managed settings, then drop to node.
#
# The image starts as root only so this phase can write the root-owned managed
# file.  Nothing else — clone, auth, dockerd, claude — runs as root: the script
# re-executes itself as node with no inheritable capabilities.  Foundry starts the
# container with every capability dropped except SETUID/SETGID for this drop
# (ADR 0088); the bounding set is not narrowed further here because a Docker-
# enabled worker's setuid newuidmap/newgidmap need the extra capabilities its
# run spec adds back.  The root filesystem is read-only, so the managed file is
# written to the root-owned tmpfs Foundry mounts at /etc/claude-code.
# ---------------------------------------------------------------------------
if [[ "$(id -u)" -eq 0 ]]; then
    render_managed_settings "$MANAGED_SETTINGS_FILE" || exit 1
    unset CLAUDE_SETTINGS_JSON
    # Docker derives HOME from the starting user (root); point it, and the
    # login names, at node's passwd entry so ~ resolves as before.
    _node_home="$(getent passwd node | cut -d: -f6)"
    export HOME="${_node_home:-/home/node}" USER=node LOGNAME=node
    unset _node_home
    exec setpriv --reuid=node --regid=node --init-groups --inh-caps=-all "$0" "$@"
fi

# Non-root phase: before any other work, prove the root phase ran.
verify_managed_settings "$MANAGED_SETTINGS_FILE" || exit 1

# Foundry no longer writes user settings.  Remove any left over (the config dir
# is container-local, but a custom CLAUDE_CONFIG_DIR may point elsewhere) so
# settings injected there never outlive the next worker start.
if ! rm -f ~/.claude/settings.json || { [[ -n "${CLAUDE_CONFIG_DIR:-}" ]] && ! rm -f "${CLAUDE_CONFIG_DIR}/settings.json"; }; then
    fail_bootstrap "managed-settings" "could not remove the stale user settings file ~/.claude/settings.json"
    exit 1
fi

# Required environment variables
# Valid auth: EITHER ANTHROPIC_API_KEY (API-key mode) OR a credential file in the
# CLI's credential store (OAuth volume mode, written by claude /login).  The CLI
# resolves that store from CLAUDE_SECURESTORAGE_CONFIG_DIR, falling back to
# CLAUDE_CONFIG_DIR.  Foundry sets only the former: the shared credential volume
# holds just the credentials, while the Claude config dir (~/.claude) stays
# container-local so one worker cannot plant CLAUDE.md, skills, agents, commands
# or settings that another worker loads (ADR 0090).
_cred_dir="${CLAUDE_SECURESTORAGE_CONFIG_DIR:-${CLAUDE_CONFIG_DIR:-}}"
if [[ -z "${ANTHROPIC_API_KEY:-}" ]] && [[ -z "$_cred_dir" || ! -f "$_cred_dir/.credentials.json" ]]; then
    echo "Authentication required: set ANTHROPIC_API_KEY (API-key mode) or mount a credentials file at \$CLAUDE_SECURESTORAGE_CONFIG_DIR/.credentials.json (OAuth mode)" >&2
    exit 1
fi
unset _cred_dir
: "${CLONE_URL:?CLONE_URL is required}"
: "${GIT_PAT:?GIT_PAT is required}"
: "${WORKER_PROMPT:?WORKER_PROMPT is required}"
: "${SYSTEM_PROMPT:?SYSTEM_PROMPT is required}"
: "${ISSUE_NUMBER:?ISSUE_NUMBER is required}"
: "${WORKER_ROLE:?WORKER_ROLE is required}"

# Resolve the run's role plugins before any clone/auth work so a role this image
# does not carry fails fast at stage=role.  The roles directory is baked into the
# image (ADR 0095); FOUNDRY_ROLES_DIR only redirects it for the bats suite.
readonly WORKER_ROLES_DIR="${FOUNDRY_ROLES_DIR:-/opt/foundry/roles}"

# The repository is always cloned to /workspace in the container; FOUNDRY_WORKSPACE_DIR
# only redirects the root for the bats suite, which also runs inside a live worker.
readonly WORKSPACE_DIR="${FOUNDRY_WORKSPACE_DIR:-/workspace}"
prepare_role_plugins "$WORKER_ROLES_DIR" "$WORKER_ROLE" || exit 1
prepare_role_verdict_schema "$WORKER_ROLES_DIR" "$WORKER_ROLE" || exit 1

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
GIT_ASKPASS="$ASKPASS_FILE" git clone "$CLONE_AUTH_URL" "$WORKSPACE_DIR"

rm -f "$ASKPASS_FILE"
unset ASKPASS_FILE
trap - EXIT

# GIT_PAT is no longer needed after clone — unset it before invoking claude
# so the token is not visible in the container's environment during the run.
# Push auth uses GH_TOKEN / GITLAB_TOKEN via the gh/glab credential helpers.
unset GIT_PAT

git -C "$WORKSPACE_DIR" remote set-url origin "$CLONE_URL"

cd "$WORKSPACE_DIR"

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

# A fixed identity leaves the agent nothing to invent when it commits: without
# one it improvised a Claude persona and credited Claude in trailers and PR text.
# --global writes ~/.gitconfig, which lives on the writable /home/node volume.
BOOTSTRAP_STAGE="git-identity"
git config --global user.name "Foundry Worker"
git config --global user.email foundry-worker@foundry.invalid

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

# Restore the repo and decide on the C# LSP plugin immediately before claude
# starts: after `unset GIT_PAT` and the gh/glab credential-helper setup, so the
# repo-controlled MSBuild evaluation sees exactly the environment the agent sees.
readonly CSHARP_LSP_RESTORE_TIMEOUT_SECONDS=300
readonly CSHARP_LSP_PLUGIN_DIR=/opt/claude-plugins/csharp-lsp
prepare_csharp_lsp "$WORKSPACE_DIR"

# ---------------------------------------------------------------------------
# Invoke claude in headless stream-json mode.  stream-json emits one event per
# line as the run progresses (the final line is the "result" event), so stdout
# is piped through tee: each line reaches the container log as it is produced
# while a copy is kept for transient-error classification.  stderr streams
# through live.  --verbose is mandatory: the CLI rejects stream-json with
# --print unless --verbose is also set.  When the role has a verdict schema,
# --json-schema (VERDICT_SCHEMA_ARGS) makes the final result event carry the
# role's verdict as structured_output (ADR 0094).
# set -e is toggled off around the pipeline so we can inspect claude's exit
# status (PIPESTATUS[0], not tee's).
#
# If the first run classifies as a transient API error (mirrors
# ContainerOutputParser.IsTransientApiError), resume the interrupted session
# ONCE via "claude -c" with the same output flags before giving up.  The
# resume's result event becomes the last result in the log — if it also fails
# transiently, Foundry's outer retry bound (#368) still applies.
# ---------------------------------------------------------------------------
# The turn budget is a cost breaker for a runaway run; the transient-error resume
# gets the same budget, so both read it from here (#798).
readonly CLAUDE_MAX_TURNS=300
CLAUDE_OUTPUT_FILE="$(mktemp)"
trap 'rm -f "${CLAUDE_OUTPUT_FILE:-}"' EXIT

set +e
claude -p "$WORKER_PROMPT" \
    --append-system-prompt "$SYSTEM_PROMPT" \
    --dangerously-skip-permissions \
    --output-format stream-json \
    --verbose \
    --max-turns "$CLAUDE_MAX_TURNS" \
    "${ROLE_PLUGIN_ARGS[@]}" \
    "${VERDICT_SCHEMA_ARGS[@]}" \
    "${CSHARP_LSP_PLUGIN_ARGS[@]}" | tee "$CLAUDE_OUTPUT_FILE"
first_status=${PIPESTATUS[0]}
set -e

if is_transient_api_error "$(cat "$CLAUDE_OUTPUT_FILE")"; then
    echo "Transient API error detected — resuming session once via claude -c" >&2
    # Resume the most recent conversation in cwd.  --append-system-prompt is
    # intentionally omitted: -c restores the original session (system prompt
    # included) and re-appending would duplicate it.  The C# LSP decision is
    # reused as-is (plugins are not part of the session); no second restore.
    # The role plugins and the verdict schema are passed again for the same
    # reason, so the resume's result event carries the role's verdict too.
    set +e
    claude -c -p "$WORKER_PROMPT" \
        --dangerously-skip-permissions \
        --output-format stream-json \
        --verbose \
        --max-turns "$CLAUDE_MAX_TURNS" \
        "${ROLE_PLUGIN_ARGS[@]}" \
        "${VERDICT_SCHEMA_ARGS[@]}" \
        "${CSHARP_LSP_PLUGIN_ARGS[@]}" | tee "$CLAUDE_OUTPUT_FILE"
    resume_status=${PIPESTATUS[0]}
    set -e
    exit "$resume_status"
fi

exit "$first_status"
