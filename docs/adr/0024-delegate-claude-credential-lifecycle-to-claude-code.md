# Delegate Claude OAuth Credential Lifecycle to the Claude Code CLI

## Context

Foundry previously snapshotted an OAuth token by scanning the host disk for `~/.claude/.credentials.json`, storing the token in the database, and injecting it as `CLAUDE_CODE_OAUTH_TOKEN` into each worker container.
The on-disk credential rotated (access-token refresh by the genuine CLI) while the snapshotted token stayed stale, producing a false "valid" status and 401 errors in dispatched workers.
Foundry had no path to refresh the token server-side without reimplementing the Anthropic OAuth client.

## Decision

Delegate the full OAuth credential lifecycle to the genuine Claude Code CLI via a Foundry-managed shared writable Docker volume.

A one-time `claude /login` seeds the volume; each worker mounts it at `CLAUDE_CONFIG_DIR` (`/home/node/.claude`) and the genuine CLI reads, uses, and auto-refreshes `.credentials.json` in place.
Foundry stores no token and injects none — the CLI owns access-token refresh (silent, persisted to the shared volume) and signals refresh-token expiry through a non-zero auth-failure exit.
An auth-failure worker exit triggers an **auth-invalid pause**: dispatch pauses, the affected issue re-queues on resume, and resume requires a manual `claude /login` to re-seed the volume.
There is deliberately no auto-resume timer — Foundry cannot detect a successful re-login server-side.
Settings and the setup wizard report only what local data proves (credential file present / approximate expiry / re-login needed).

## Considered Options

- **Foundry reimplements the OAuth client** (read refresh token, call token endpoint, write new access token) — rejected: this is the ToS-restricted impersonation pattern Anthropic explicitly targeted in February 2026 by invalidating tokens issued to non-genuine clients.
- **`claude setup-token` / `sk-ant-oat01` tokens** — rejected: this command produces the non-refreshing `sk-ant-oat01` token type, which is the blocked pattern.
- **Raw-HTTP probe for token validation** — rejected: this is precisely the path Anthropic blocks; using it as a pre-dispatch health check would fail silently or produce misleading status.
- **Host bind-mount for the credential directory** — rejected: Windows-to-Linux UID mapping and the `0600` permission requirement on `.credentials.json` do not map reliably across host OSes; a Docker-managed volume is portable.
- **Keep scan-and-snapshot with improved validation** — rejected: stale snapshot is the root cause, not the validation logic; any validation Foundry performs without genuine-CLI involvement produces the same false-valid problem.

## Consequences

- The OAuth credential sits in plaintext in the Docker volume, consistent with how the genuine CLI stores it locally.
  This is a downgrade from the DB-encrypted API key, accepted because: the volume is scoped to the Docker socket trust boundary Foundry already operates within, and no alternative exists that lets the genuine CLI manage refresh without reading a plaintext credential.
- Auth-invalid resume is manual-only — there is no server-side signal Foundry can poll to detect a successful re-login.
  Operators must run `claude /login` (or equivalent) against the shared volume and then manually resume dispatch.
- Closure of the genuine-CLI OAuth path (e.g., Anthropic deprecating the shared-volume pattern) is a product escalation, not a code fallback — there is no automated recovery path Foundry can implement within ToS constraints.
