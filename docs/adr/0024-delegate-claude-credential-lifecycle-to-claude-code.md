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
Settings and the setup wizard report only what local data proves (credential file present / re-login needed).

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
  Operators must run an in-app login session (see Phase 2 below) to re-seed the volume; Foundry then auto-resumes dispatch.
- Closure of the genuine-CLI OAuth path (e.g., Anthropic deprecating the shared-volume pattern) is a product escalation, not a code fallback — there is no automated recovery path Foundry can implement within ToS constraints.

## Phase 2 — In-App Interactive Login

Phase 1 left the credential-seeding step as a manual `docker run` command outside Foundry.
Phase 2 brings the full OAuth login flow inside the Foundry dashboard, removing the manual step entirely.

### Mechanism

Foundry starts a dedicated `foundry-claude-login` container via the Docker API (`Tty: false`, credential volume mounted).
The container entrypoint bootstraps a named FIFO at `/tmp/ci`, records the FIFO writer's sleep-holder PID in `/tmp/ci.pid`, then invokes `claude auth login --claudeai` with the FIFO bound to stdin.
Foundry streams the container stdout/stderr and extracts the authorization URL from the `visit:` line; the URL is pushed to the dashboard over SignalR.
The operator opens the URL, authorizes in the browser, and pastes the code into the dashboard.
Foundry delivers the code via `docker exec` (`printf '%s\n' "$C" > /tmp/ci; kill $(cat /tmp/ci.pid)`) — the code is passed as environment variable `C`, never interpolated, so shell metacharacters cannot cause injection; the sleep-holder kill forces EOF on stdin so the CLI proceeds to token exchange without waiting.
Foundry polls the log stream for `Login successful.` and reads the container exit code (0 = success, non-zero = bad or expired code).
On success the login container has already exited (its entrypoint `exec`s the CLI as PID 1, so the container stops when login completes), so Foundry captures the authenticated account identity (email, org name, subscription type) by running `claude auth status --json` in a fresh short-lived helper container that mounts the credential volume, rather than exec-ing into the stopped login container. See [ADR 0027](0027-credential-volume-read-via-helper-container.md).

### CLI output coupling

The flow depends on specific output strings emitted by `claude` CLI 2.1.187:

| Signal | Source | Used for |
|---|---|---|
| URL containing `/oauth/` | stdout `visit:` line | Authorization URL extraction (regex match) |
| `Login successful.` | stdout | Success detection (log stream scan) |
| Non-zero exit code | container exit | Bad/expired code detection (fallback to exit code) |

The onboarding seed (`hasCompletedOnboarding`, `hasTrustDialogAccepted`, `theme`) also depends on `.claude.json` key names stable across CLI versions.
The CI integration test (`LoginIntegrationTests`) is the safety net for CLI version drift — it runs in the dedicated `login-integration` CI job and will fail if any of these strings or behaviors change.
The `login-integration` job triggers on changes to `workers/Dockerfile.base`, `workers/Dockerfile.login`, or the login source/test paths, and on manual `workflow_dispatch`.
The per-PR `api` job does not build the login image, so these tests self-skip there (image absent) and only execute in the dedicated job.

### Onboarding seed

Before invoking `claude auth login --claudeai`, the entrypoint merges onboarding-gate flags into the volume's `.claude.json` using set-if-absent semantics, so existing credentials and `oauthAccount` data are never overwritten.

### Dispatch pause

Dispatch is transiently suppressed via `ILoginSessionState.IsLoginActive` while a session is active.
This is not persisted — a Foundry restart mid-login drops the in-memory session, and `LoginContainerReaper` reaps any orphaned login containers on startup.
A successful login auto-resumes any active auth-invalid pause by calling `GlobalSettings.ResumeDispatch()` and publishing `DispatchResumed`.
