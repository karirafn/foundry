# Composed-host smoke test fixtures

## `implement-run.stream.jsonl`

The container log the smoke test's fake worker container returns after it exits 0. It is a real,
trimmed and redacted capture of `claude` CLI output — not hand-written — so the smoke test proves
Foundry's post-exit tracking (result parsing, run summary, verdict reading) against what the CLI
actually emits. A CLI release that changes the stream-json format fails the smoke test; that is the
intended signal to refresh this file.

### Capture

Refresh with [`capture-implement-run.sh`](capture-implement-run.sh) (needs an authenticated `claude`
and `jq` on `PATH`; spends one short model turn):

```bash
tests/Foundry.IntegrationTests/Smoke/Fixtures/capture-implement-run.sh
```

The script runs `claude` with the same flags as the first invocation in `workers/entrypoint.sh`:

```bash
claude -p "$WORKER_PROMPT" \
    --append-system-prompt "$SYSTEM_PROMPT" \
    --dangerously-skip-permissions \
    --output-format stream-json \
    --verbose \
    --max-turns 300 \
    --json-schema "$SCHEMA"
```

- `$SCHEMA` is `workers/roles/implement/verdict.schema.json` with the `$defs` of
  `workers/roles/shared/verdict.defs.json` merged in, by the same `jq` program as the entrypoint's
  `prepare_role_verdict_schema`.
- `$WORKER_PROMPT` and `$SYSTEM_PROMPT` are short synthetic prompts asking for a one-sentence reply
  and the verdict `done`, and the CLI runs in an empty scratch directory, so no repository or issue
  content can enter the transcript.
- The role and C# LSP plugin arguments are omitted: they add tools, not output format.

Captured with Claude Code `2.1.316` (recorded in the init event).

### Redaction

Applied by the script's `jq` filter:

| Field | Replacement |
|-------|-------------|
| `session_id`, `uuid` | `00000000-0000-0000-0000-000000000000` |
| `request_id` | `req_redacted` |
| `message.id` | `msg_redacted` |
| tool-use ids (`content[].id`, `content[].tool_use_id`, `tool_result_meta[].id`) | `toolu_redacted` |
| `rate_limit_event` events (account usage utilisation) | dropped |
| `system`/`init` event | trimmed to `type`, `subtype`, `cwd`, `session_id`, `model`, `permissionMode`, `apiKeySource`, `claude_code_version`, `output_style`, `uuid`; the capturing machine's tool, skill, plugin, agent and MCP server lists are dropped and `cwd` becomes `/workspace` |

Everything else — event order, the assistant and tool-result events, and the final `result` event
with its `usage`, `modelUsage` and `structured_output` — is the CLI's output unchanged.
