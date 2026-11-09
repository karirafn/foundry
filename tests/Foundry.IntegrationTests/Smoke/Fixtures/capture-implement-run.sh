#!/usr/bin/env bash
# Captures the composed-host smoke test's stream-json fixture from the real claude CLI.
# See README.md in this directory for what is captured, why, and what is redacted.
#
# Usage: capture-implement-run.sh [output-file]
# Requires: claude (authenticated) and jq on PATH. Spends one short model turn.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../../.." && pwd)"
roles_dir="$repo_root/workers/roles"
output_file="${1:-$script_dir/implement-run.stream.jsonl}"

# The implement role's verdict schema with the shared $defs merged in — the same jq program
# workers/entrypoint.sh (prepare_role_verdict_schema) runs before passing --json-schema.
defs_json="$(jq -ce 'if type == "object" then . else error("not an object") end' "$roles_dir/shared/verdict.defs.json")"
schema="$(jq -ce --argjson shared "$defs_json" '
    if type != "object" then error("not an object") else . end
    | if ($shared["$defs"] // {}) == {} then .
      else .["$defs"] = (($shared["$defs"] // {}) + (.["$defs"] // {})) end
' "$roles_dir/implement/verdict.schema.json")"

# An empty scratch directory, so no repository content can reach the transcript.
scratch_dir="$(mktemp -d)"
raw_file="$(mktemp)"
trap 'rm -rf "$scratch_dir" "$raw_file"' EXIT

worker_prompt="This is a fixture capture run: there is no repository and no issue to work on. Do not use any tools other than the one that reports your verdict. Reply with one short sentence, then report the outcome done."
system_prompt="You are a Foundry worker in a fixture capture run."

# The same output flags as workers/entrypoint.sh's first claude invocation. The role plugins and the
# C# LSP plugin are omitted: they add tools, not output format.
(
    cd "$scratch_dir"
    claude -p "$worker_prompt" \
        --append-system-prompt "$system_prompt" \
        --dangerously-skip-permissions \
        --output-format stream-json \
        --verbose \
        --max-turns 200 \
        --json-schema "$schema" < /dev/null > "$raw_file"
)

redacted_uuid="00000000-0000-0000-0000-000000000000"

jq -c --arg uuid "$redacted_uuid" '
    # Account-level rate-limit utilisation is not part of the run contract.
    select(.type != "rate_limit_event")
    | if has("session_id") then .session_id = $uuid else . end
    | if has("uuid") then .uuid = $uuid else . end
    | if has("request_id") then .request_id = "req_redacted" else . end
    | if .message.id? then .message.id = "msg_redacted" else . end
    | if (.message.content? | type) == "array" then
        .message.content |= map(
            (if has("id") then .id = "toolu_redacted" else . end)
            | (if has("tool_use_id") then .tool_use_id = "toolu_redacted" else . end))
      else . end
    | if has("tool_result_meta") then .tool_result_meta |= map(.id = "toolu_redacted") else . end
    # The init event lists the capturing machine'"'"'s tools, skills, plugins and agents: keep only
    # the fields that describe the run, with the scratch directory replaced by the worker path.
    | if .type == "system" and .subtype == "init" then
        {type, subtype, cwd: "/workspace", session_id, model, permissionMode, apiKeySource,
         claude_code_version, output_style, uuid}
      else . end
' "$raw_file" > "$output_file"

echo "Wrote $(wc -l < "$output_file") events to $output_file" >&2
