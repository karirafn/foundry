# Extract Claude Worker Credentials into a Credentials Module

## Context

The Claude worker-credential capability is split across two modules. The Workers module owns the login state machine (`LoginSessionService`), OAuth code delivery, credential-volume access, and auth-invalid detection; the Settings module owns the persisted state on `GlobalSettings` (auth mode, OAuth account identity, auth-invalid pause). Two cross-module writes bridge the split: `LoginSuccessCommitter` and `WorkerDispatchService.PersistAuthInvalidIfNeeded` both reach into `GlobalSettings` and `SaveChanges`. [ADR 0026](0026-login-session-service-in-workers-module-transient-dispatch-pause.md) recorded this Workers-owns-session / Settings-owns-state arrangement — including the narrow one-way Workers → Settings write — as an intentional trade-off to avoid a module dependency cycle.

That arrangement has since accumulated the smells that boundary predicts: auth-invalid rules scattered across three locations (detect, suppress, resume), the credential volume once read two different ways that diverged and caused real bugs, and `DockerWorkerOrchestrator` conflating worker orchestration with login/credential operations. The capability is a distinct bounded context — "the Claude account credentials workers authenticate with" — separate from provider PATs (Monitoring's `Account`) and from app-user auth (planned in #178).

## Decision

Extract the capability into its own `Foundry.Modules.Credentials` module (with a `Credentials.Contracts` companion), owning a `ClaudeAccount` aggregate: auth mode, OAuth account identity, and a **credential validity state** (`Valid | Invalid(reason) | LoginInProgress`). This reverses ADR 0026's placement — `LoginSessionService` and the persisted credential state now live together in one module — and supersedes it once implemented (tracked by #267 → #268).

Three principles define the boundary:

- **Owned validity, composed by dispatch.** The module owns credential validity and exposes it via an `ICredentialGate` read. `WorkerDispatchService` composes its dispatch decision from `credentials-valid ∧ no-active-login ∧ manual-pause ∧ usage-limit` — each pause reason owned by whoever owns that concern. The `AuthInvalidPause` boolean and the separate `ILoginSessionState` read are both replaced by this single gate.
- **No cross-module aggregate mutation.** The two Workers → Settings writes become integration events. A worker exiting auth-invalid makes Workers publish `WorkerAuthenticationFailed`; the module transitions validity and publishes `CredentialsInvalidated`. A successful login makes the module write identity and publish `CredentialsValidated`, which Issues consumes to re-queue auth-invalid-failed work. This keys re-queueing off credential re-validation rather than the generic `DispatchResumed` (which also fires on manual resume, where credentials may still be invalid).
- **Shared Docker infrastructure first.** The low-level Docker primitives (container create/exec/logs, volume ops, in-container file I/O) are extracted from `DockerWorkerOrchestrator` into a shared infrastructure abstraction consumed by both Workers and Credentials, as a prerequisite change. This prevents recreating the duplicate-implementation divergence that previously caused bugs.

`LoginInProgress` remains transient (in-memory on the session singleton), consistent with ADR 0026's transient-pause reasoning; only its ownership moves into the module, folded into the composed validity state.

## Considered Options

- **Lite refactor — invert the two mutations to events and consolidate the gate, without a new module or data move.** Achieves the same correctness (no cross-boundary mutation, no divergence, single gate) with a smaller diff. Rejected in favour of durable single ownership and compiler-enforced `internal` boundaries (per [ADR 0006](0006-module-assembly-extraction.md)), accepting the larger diff and data migration.
- **Narrow scope — module owns login mechanics only; auth mode and pause stay on `GlobalSettings`.** Rejected: leaves the auth-invalid rules scattered and keeps both cross-boundary mutations.
- **Module owns a dispatch-pause flag rather than validity state.** Rejected: merely relocates the cross-boundary write into a dispatch concern; owning validity and letting dispatch compose keeps each state with its owner.
- **Absorb usage-limit pause into an "account availability" state.** Structurally symmetric, but a quota concern is not an auth concern (different lifecycle: auto-resume on a timer vs. explicit login). Deferred as a separate, later extraction rather than broadening this boundary and its name.
- **Give the new module its own Docker client and duplicate the primitives it needs.** Rejected: revives the two-implementations divergence that already burned this codebase.
- **Name the module `ClaudeAuth` / `WorkerAuth`.** Rejected in favour of `Credentials` to match the single-noun sibling convention, avoid the `Workers`-module confusion, and steer clear of the "authentication" token that #178 will use for app-user auth.

## Consequences

- ADR 0026 is superseded on implementation: `LoginSessionService` moves out of Workers, and the intentional Workers → Settings write it documented is replaced by event choreography. The implementing PR marks 0026 accordingly.
- `GlobalSettings` sheds its auth fields; `GetAuthModeAsync` / `GetAuthEnvironmentVariableAsync` move from `IGlobalSettingsQueries` to `Credentials.Contracts`, and `IssueClaimedHandler` re-points there.
- A new entity configuration and table join the shared `FoundryDbContext` (no separate DbContext, matching ADR 0006); the migration copies the existing single `GlobalSettings` row's auth values into `ClaudeAccount`.
- The work is sequenced as two PRs — shared Docker infrastructure (#267), then the module extraction (#268, blocked by #267) — and #267 itself lands after #262 and #255, which are in flight on the same Docker code.
- Adding a new integration-event pair (`CredentialsValidated` / `CredentialsInvalidated`) plus `WorkerAuthenticationFailed` widens the Contracts surface but removes shared-`DbContext` cross-module writes.
