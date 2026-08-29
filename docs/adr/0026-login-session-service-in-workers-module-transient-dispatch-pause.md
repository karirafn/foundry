# Login Session Service Placement in Workers Module and Transient In-Memory Dispatch Pause

> **Status: superseded by [0030](0030-extract-claude-credentials-module.md).** The Claude worker-credential capability — including `LoginSessionService` and the credential validity state — moved into the `Foundry.Modules.Credentials` module (#268). The one-way Workers → Settings write documented below is replaced by integration-event choreography (`WorkerAuthenticationFailed` → `CredentialsInvalidated`; login → `CredentialsValidated`). The transient in-memory login-in-progress reasoning still holds, now composed inside `ICredentialGate`.

## Context

The interactive OAuth login feature (Phase 2 of #208) requires a singleton service that manages the active login session and a mechanism to suppress worker dispatch while a login is in progress.
Two placement decisions had non-obvious trade-offs worth recording: which module owns `LoginSessionService`, and whether the dispatch pause is persisted or transient.

## Decision

### Service placement: Workers module, not Settings

`LoginSessionService` lives in `Foundry.Modules.Workers`, not `Foundry.Modules.Settings`.

The login session drives Docker operations — starting a login container, streaming its logs, executing into it via the Docker socket, and tearing it down.
All of these operations are implemented on `IWorkerOrchestrator`, which is owned by the Workers module.
Placing the service in Settings would require Settings to reference Workers infrastructure, introducing a module dependency cycle (Workers already references the Settings domain for `GlobalSettings` reads/writes on login success).

The Settings module remains the owner of persisted OAuth state (`GlobalSettings.OAuthAccountEmail`, `IsAuthInvalidPaused`, etc.).
The Workers module owns the transient session that produces that state.

`LoginSuccessCommitter` (Workers) crosses the boundary at commit time: it opens a scoped `DbContext` to persist the account identity and clear the auth-invalid pause on `GlobalSettings`, then publishes `DispatchResumed`.
This is the only direction of cross-module write — Workers → Settings domain, via the shared `DbContext` — and it is intentionally narrow and one-way.

### Dispatch suppression: transient in-memory, not persisted

While a login session is active, `WorkerDispatchService` skips issuing new work by consulting `ILoginSessionState.IsLoginActive`.
This state is held only in memory on the `LoginSessionService` singleton — no database row, no `GlobalSettings` flag.

The pause is intentionally transient because:

- A login session is a short-lived interactive operation (seconds to a few minutes); persisting a "login in progress" flag would require explicit cleanup logic on success, failure, and every crash/restart path.
- A Foundry restart mid-login is the natural recovery path: the in-memory session is gone, `LoginContainerReaper` reaps the orphaned login container on startup, and dispatch resumes without any flag to clear.
- Persisting the pause would conflict with the pre-existing auth-invalid pause (`IsAuthInvalidPaused` on `GlobalSettings`): two independent pause sources with different semantics competing for the same flag would complicate resume logic.

Dispatch held by an auth-invalid pause (`IsAuthInvalidPaused`) is unaffected by a Foundry restart — that flag is persisted and survives restarts, as intended.

## Considered Options

### Settings module placement

Placing `LoginSessionService` in Settings was the first candidate because the login flow's visible outcome (account identity, auth-invalid resume) lands on `GlobalSettings`.
Rejected because it would require the Settings module to take a dependency on `IWorkerOrchestrator` and the Docker abstraction layer — a hard inversion of the existing dependency direction.
The only viable workaround would have been to abstract the Docker operations behind an interface defined in Settings, which would replicate the Workers module's orchestrator abstraction in a second module solely to avoid the cycle.

### Persisted login-active flag on GlobalSettings

Adding an `IsLoginInProgress` boolean to `GlobalSettings` would have survived Foundry restarts and allowed the dashboard to reflect in-progress state on page reload.
Rejected because:

- The dashboard uses SignalR to track session phase; a reload while a session is active simply starts the session fresh (which is already idempotent).
- Clearing the flag reliably on crash/restart paths requires the same `LoginContainerReaper` logic already in place, so persistence adds cost without removing the reaper dependency.
- It adds a `GlobalSettings` migration for what is operationally a seconds-long transient state.

## Consequences

- A Foundry restart mid-login silently drops the session. The operator sees no active session on reconnect and must start a new one. `LoginContainerReaper` removes the orphaned container so no Docker resources leak.
- Pre-existing auth-invalid pauses (`IsAuthInvalidPaused`) are unaffected by a restart — they survive as intended. The login session pause and the auth-invalid pause are fully independent.
- `ILoginSessionState` is a public interface (referenced by `WorkerDispatchService` across the module boundary via DI). `LoginSessionService` implements it as an `internal sealed class` — tests inject a stub via the interface, production DI resolves the real singleton.
- Adding a UI "login in progress" indicator on page load requires a `GET /api/settings/oauth/login/status` endpoint that reads `LoginSessionService.IsLoginActive` — no persistence needed.

## Security / Trust Boundary

The HTTP surface for the login flow — including `POST /api/settings/oauth/login/start` and `POST /api/settings/oauth/login/code` — is **unauthenticated at the application layer**.
Foundry relies on network-level isolation to the operator; it is not designed for exposure to untrusted networks.

The OAuth authorization code transits Foundry only in memory (never logged, never written to the database or disk).
It is delivered to the login container via a `docker exec` environment variable (`C=<code>`), which writes into the pre-created FIFO via `printf '%s\n' "$C" > /tmp/ci`.
The code is never interpolated into the shell command string in argv.

**Residual risk — delivery exec inspection window:** The `C` environment variable is briefly visible via `docker inspect` / `GET /exec/{id}/json` during the ~millisecond delivery window.
This is an accepted residual risk bounded by the Docker-socket trust boundary: an attacker able to inspect the exec already controls the daemon and can read the credential volume directly.

**Cross-platform constraint:** Stream-stdin delivery (`docker exec` stdin) is not viable.
`Docker.DotNet`'s `MultiplexedStream.WriteAsync` does not land on the Windows npipe transport — bytes written to the exec's stdin stream never reach the container.
The env-var approach was the spike-proven cross-platform design from the outset.

Accepted residual risks (unchanged from [ADR 0024](0024-delegate-claude-credential-lifecycle-to-claude-code.md)):

- The seeded `.credentials.json` sits in plaintext inside the Docker-managed credential volume, consistent with how the Claude Code CLI stores credentials locally on the host.
- Foundry operates within the Docker socket trust boundary; any process with access to the Docker socket can read the credential volume.

These trade-offs were evaluated in ADR 0024 and are accepted as inherent to the genuine-CLI delegation model.
