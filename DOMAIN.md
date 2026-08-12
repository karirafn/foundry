# DOMAIN.md

Domain concepts for Foundry — a containerized service that monitors repositories for tagged issues and dispatches sandboxed AI workers to implement them.

---

## Foundry

The service itself.
Monitors repositories across multiple providers for issues with a trigger label, then dispatches sandboxed Claude Code Docker containers to implement them.

## Worker

A Claude Code Docker container dispatched to implement a single issue.
Ephemeral — created on demand, destroyed after completion.
Has no identity independent of its WorkerRun — the container is the execution mechanism, not a separate domain concept.
Workers clone the repo, implement the issue, push a branch, and open a PR. Foundry discovers each run's outcome by polling the container and querying the provider after the container exits.

## Worker Authentication

How a worker container authenticates with the Anthropic API (Claude Code).
Two methods: API key (`ANTHROPIC_API_KEY`, pay-per-use, stored encrypted in DB) and OAuth (Max/Pro/Team/Enterprise plan, managed via a shared Docker volume).
Exactly one method is configured per Foundry instance — selected via auth mode in Global Settings.

OAuth mode delegates the full credential lifecycle to the genuine Claude Code CLI.
Each worker mounts a Foundry-managed, shared, writable Docker volume at its Claude config dir (`CLAUDE_CONFIG_DIR` → `/home/node/.claude`) and the CLI reads, uses, and auto-refreshes `.credentials.json` in place.
Foundry stores no token and injects none — the CLI is solely responsible for access-token refresh (silent, persisted to the shared volume) and for detecting refresh-token expiry.

**In-app interactive login.**
Operators seed the credential volume from within the Foundry dashboard via an interactive OAuth flow, without running any Docker command manually.
Foundry starts a dedicated login container (`foundry-claude-login` image) using the Docker API with `Tty: false`.
The container's entrypoint bootstraps a named FIFO at `/tmp/ci`, writes the FIFO's writer sleep-holder PID to `/tmp/ci.pid`, then starts `claude auth login --claudeai` — wrapped in `timeout -k 10 <session-timeout>` — with the FIFO wired to stdin so the process blocks waiting for the authorization code.
The `timeout` wrapper bounds the CLI's lifetime independently of the sleep-holder (which the code-delivery exec kills), and the container is created with `AutoRemove: true`, so it self-terminates and Docker removes it even if Foundry is not alive to tear it down — leaks cannot outlive the session timeout.
Foundry streams the container's stdout/stderr log lines and extracts the `https://…/oauth/…` authorization URL (the `visit:` line emitted by the CLI) — the URL is broadcast to the dashboard via SignalR as a `LoginSessionUpdate`.
The operator opens the URL, authorizes, and pastes the code into the dashboard.
Foundry delivers the code into the FIFO via `docker exec` (`printf '%s\n' "$C" > /tmp/ci`), then kills the sleep-holder process (read from `/tmp/ci.pid`) so the FIFO writer closes and the CLI receives EOF on stdin and proceeds to token exchange.
The service scans the log stream for the `Login successful.` signal, which is authoritative — an `Invalid code` line, or the stream closing without the success signal, is treated as failure.
It deliberately does not gate on the container exit code: the CLI may still be running when the success signal appears (observed in practice living seconds longer), so an exit-code check races the signal and misreports success as failure.
Because the login container's lifecycle at that moment is therefore unreliable (it may or may not have exited), Foundry captures the authenticated account's email, org name, and subscription type without depending on it — by running `claude auth status --json` in a fresh short-lived helper container that mounts the same credential volume read-only, then tears the helper down.
This volume read is also the real confirmation that login persisted a valid credential.
On an invalid code the session transitions to `Failed(InvalidCode)` and is broadcast to the dashboard for re-prompt — the operator may start a new session.

**Onboarding seed.**
Before starting `claude auth login --claudeai`, the entrypoint idempotently merges onboarding-gate flags into the volume's `.claude.json` (`hasCompletedOnboarding`, `hasTrustDialogAccepted`, `theme`), setting each key only when absent, so existing credentials and account data are never overwritten.

**Session lifecycle.**
At most one login session is active at a time — `LoginSessionService` (singleton, in-memory, Credentials module) enforces this.
Dispatch is transiently suppressed while a session is active via `ILoginSessionState.IsLoginActive` (checked by `WorkerDispatchService`); no pause record is persisted.
Session state is not persisted: a Foundry restart mid-login leaves the in-memory session gone.
In-process, `LoginSessionService.SubmitCodeAsync` tears the container down in a `finally`, so every exit path — success, invalid code, timeout, host shutdown — removes it exactly once (a second remove is a safe no-op).
For crashes where no in-process path runs, two reapers back it up: `LoginContainerReaper` (an `IHostedLifecycleService`) reaps orphaned transient containers of any state on startup — safe because no session is active yet — and `TransientContainerReaper` (a periodic background service, 60 s) sweeps only *exited* transient containers (the residue when `AutoRemove` misses), so it never removes a live login or in-flight helper while a session is active.
Both — and all transient login and credential-helper containers — key off the `foundry.transient=true` label, deliberately distinct from the `foundry.managed`/`foundry.worker-run-id` labels on long-lived worker containers, so reaping never touches a running worker.
A session times out if no URL is captured within the URL timeout, if no code is submitted within the session timeout, or if login confirmation is not seen within the sign-in timeout after the code is delivered; these surface as typed `LoginFailureReason` variants (`UrlTimeout`, `CodeTimeout`).

**Auth-invalid pause.**
When a worker exits with an auth failure, Foundry classifies the run as `AuthInvalid` and raises an **auth-invalid pause**: dispatch pauses, the affected issue is retried automatically on resume, and resume is triggered automatically by a successful in-app login (which calls `GlobalSettings.ResumeDispatch()` and publishes `DispatchResumed`).
There is deliberately no auto-resume timer for auth-invalid — Foundry cannot detect a successful re-login without an explicit in-app login session completing.
Settings and the setup wizard derive OAuth status from persisted state — a committed account identity (set by a successful in-app login) reads as signed-in, an auth-invalid pause reads as re-login-needed — rather than from a live credential-volume read; token expiry is deliberately not surfaced (the CLI auto-refreshes it in place, so a captured value would be stale and misleading), and they never assert an unverified "valid" status.
The OAuth credential sits in plaintext in the volume, consistent with how the genuine CLI stores it, and bounded by the Docker-socket trust boundary Foundry already operates within.
Distinct from provider authentication (Account / PAT), which authenticates git operations against GitHub or GitLab.

## Worker Image Build

The staged Docker build (base → worker → login images) that produces the images workers run on, executed by `WorkerImageRebuildService` (a Workers-module background service).
A failure at any stage — including the login image — fails the whole rebuild.
Rebuilds are requested when the Worker Image Configuration changes (`WorkerImageConfigurationChanged` → immediate rebuild request) and by operator retry; an immediate rebuild request supersedes a pending backoff wait.
While no usable image exists and at least one account is configured, the dashboard blocks with a full-screen forge overlay (**cold build**) until the first build succeeds.

## Image Build State

A closed-hierarchy value object on Global Settings tracking the worker image build lifecycle: `Idle`, `Building`, `Failed(ErrorTail, NextRetryAt, Attempt)`.
Transitions are methods on the aggregate (`BeginImageBuild`, `CompleteImageBuild`, `FailImageBuild`); persisted as a JSON blob.
Failed builds auto-retry with exponential backoff (`initial * 2^(attempt-1)`, capped at a configured maximum); operator retry is only permitted from `Failed`.
A non-`Idle` state drives the dashboard image-build banner and disables the worker-image settings form, and dispatch gates on the status — no new workers are dispatched while the image is building or failed.

## Worker Image Configuration

The set of preinstall flags (provider CLIs and other tooling, e.g. `INSTALL_GH`, `INSTALL_GLAB`) stored on Global Settings, driving Docker build args for the worker image.
Changing it publishes `WorkerImageConfigurationChanged`, which requests an immediate rebuild.

## Provider

A git hosting platform (GitHub, GitLab).
Each provider has its own API client, label format (flat on GitHub, scoped on GitLab), and CLI tooling (`gh`, `glab`).
The provider is carried on `ClaimedIssueDispatch` so the worker dispatch knows which CLI tooling and credential env var apply.
Inside the worker, the provider CLI is registered as git's credential helper (e.g. `gh auth setup-git`), authenticated by the Account PAT injected as the provider-specific token env var (`GH_TOKEN` for GitHub, `GITLAB_TOKEN` for GitLab) — this authenticates both `git push` and PR/MR creation without embedding the PAT in the repo's git config.
The provider CLI is baked into the worker image at build time via per-provider build-arg flags (`INSTALL_GH`, `INSTALL_GLAB`); when the flag is off the entrypoint warns rather than failing silently.

## Branch Protection

A provider-agnostic set of preconditions Foundry requires on a repository's default branch before processing issues.
Three invariants: rejects direct pushes (all changes via merge/pull request), rejects force pushes (history cannot be rewritten), rejects deletion.
Each provider adapter maps these invariants to provider-specific API checks (GitHub branch protection rules, GitLab protected branches).
Validated per repository on each poll cycle and at repository creation, with the result stored on the Monitored Repository as its Repository Eligibility. Dispatch is gated on the repository's eligibility; individual issues are not re-validated.

## Account

A credential for accessing a specific provider's API — one Account per PAT ("Account" in the UI, `Credential` in the domain).
Modeled as polymorphic variants (`GitHubCredential`, `GitLabCredential`) — each provider may carry provider-specific configuration (e.g., API base URL for self-hosted instances).
The PAT is stored encrypted in the database using Data Protection API + EF Core Value Converters.
Multiple accounts can exist per provider and per host — including multiple PATs authenticating as the same provider user. Accounts do not reference repositories; repositories resolve to an account through Namespace Claims.

### PAT Owner

The provider username the token authenticates as — resolved from the provider's `/user` endpoint at token validation and stored as the account's name (`Credential.Name`).
Not user-chosen: the account's display name *is* the PAT owner. Because multiple accounts may share one PAT owner, the name alone does not identify an account; UI surfaces disambiguate with the account's Namespace Claims and host.

### Token Validation Outcome

A closed variant set (`TokenValidationOutcome`) returned by both provider adapters after validating a PAT.
Five variants:

- `Authenticated(AccountName, MissingScopes)` — the token authenticated and the PAT owner resolved; `MissingScopes` lists any required scopes absent from the token.
- `AuthenticationFailed` — the token was rejected (401/unauthorized).
- `ScopesUnverifiable(AccountName)` — the token authenticated and the owner resolved, but the token's scopes could not be read (e.g. GitLab group/project access tokens where `personal_access_tokens/self` returns non-2xx, or GitHub classic PATs served without `X-OAuth-Scopes`).
- `IdentityUnresolved` — the response could not be parsed to an identity (neither provider's identity field present).
- `ProviderMismatch(DetectedProvider)` — the response carried the *other* provider's identity shape (e.g. GitLab answered while GitHub was selected); names the detected provider.

Two behavioral rules apply:

1. A successful authentication requires a PAT owner — a "valid but nameless" state cannot be represented; if the owner cannot be resolved the outcome is `IdentityUnresolved` or `ProviderMismatch`, not `Authenticated`.
2. `ScopesUnverifiable` warns but permits saving — the provider owns authorization and claim-time scope enforcement is the real gate, so an unreadable scope list is advisory, not blocking.

The endpoint (`POST /api/accounts/validate-token`) maps these variants to the response `Kind` field: `"authenticated"`, `"authenticationFailed"`, `"scopesUnverifiable"`, `"identityUnresolved"`, `"providerMismatch"`.

## Namespace Claim

The exclusive association between an Account and an owner namespace on a host — stored in `credential_namespaces` with a unique `(host, namespace)` constraint, so each namespace is served by exactly one account.
Claims are derived from the token's writable-repository listing (every distinct owner of a repo the token can push to) at account creation, token rotation, and repository recheck.
A Monitored Repository carries no account reference; on each eligibility evaluation the covering account is resolved by matching the repository's owner against claims (`ICredentialResolver`). A repository whose owner no account claims is Ineligible (`no-credential:<namespace>`).

## Global Settings

A strongly-typed single-row entity storing all UI-configurable settings.
Includes worker settings (max concurrent, timeout, prompt templates), authentication mode (API key or OAuth), the Worker Image Configuration and Image Build State, and dispatch pause controls: usage-limit controls (`AutoResumeOnUsageReset`, `UsageLimitResetsAt`) and the auth-invalid pause (`IsAuthInvalidPaused`) — both pause dispatch until explicitly resumed, but only the usage-limit pause supports auto-resume. `IsDispatchPaused` is the separate manual operator pause.
DB is the single source of truth — `IConfiguration` is not consulted for settings the UI manages.
Infrastructure-only settings (Docker image, mounts, memory/CPU/PID limits) remain in `IConfiguration`.

## Monitor

The background process that polls configured repositories for issues labeled `foundry`.
Runs on a fixed tick interval (30s default) and checks whether each repo is due for polling based on its configured poll interval and `LastPolledAt` timestamp.

## Issue

A provider-side issue tagged for Foundry processing.
Modeled as a polymorphic aggregate — each lifecycle state is a distinct type (`DetectedIssue`, `BlockedIssue`, `QueuedIssue`, `ContinuationQueuedIssue`, `RevisionQueuedIssue`, `InProgressIssue`, `RevisionInProgressIssue`, `ReviewIssue`, `UnchangedIssue`, `CompletedIssue`, `FailedIssue`, `ContinuableFailedIssue`, `RevisionFailedIssue`).
State transitions are methods on each variant that return the next variant type, enforcing valid transitions at compile time.

### Issue Lifecycle Partition

The lifecycle splits into two partitions defined by `IssueStateRegistry` (server) and mirrored in `ACTIVE_STATES` / `RESOLVED_STATES` (frontend):

- **Active** — every state except `completed`.
- **Resolved** — `completed` only.

The dashboard renders Active states in four display groups, classified by one rule — the relationship between the issue and a worker:

- **In progress** — a worker is actually running right now (`in_progress`, `revision_in_progress`). Exactly `LIVE_STATES`.
- **Needs attention** — requires a user action to progress (`review`, `unchanged`, `failed`, `continuable_failed`, `revision_failed`).
- **Waiting** — waiting for a worker; advances with no user action (`detected`, `queued`, `blocked`, `revision_queued`, `continuation_queued`).
- **Resolved** — done (`completed`).

Two states deserve explicit note:

- `unchanged` is Active/Needs-attention because the worker produced no changes and the user must decide whether to retry — it cannot resolve itself.
- `blocked` is Waiting because a `BlockedIssue` auto-transitions to `QueuedIssue` when its blockers close on the provider; no user action is required.

## Issue Kind

A value object on the base `Issue` type classifying the nature of the work — `Feature`, `Bug`, `Refactor`, `Documentation`, etc.
Extracted during issue detection by a provider-agnostic classifier: each label is normalized by stripping any `scope::` prefix, then the suffix is matched against the kind names — so flat (`feature`) and scoped (`type::feature`) labels both classify on either provider.
Falls back to `Feature` when no recognized label is present.
Used by `BranchName.Generate()` to derive the branch prefix (`feat/`, `fix/`, `refactor/`, `docs/`).

## Monitored Repository

A repository configured for Foundry to poll.
Resolves its serving Account through the Namespace Claim on its owner (no stored account reference) and specifies an optional per-repo poll interval.
Uniquely identified by the pair (Host, Repository Slug) — the same repo on the same host cannot be monitored twice (prevents duplicate issue detection), while the same path on different hosts (e.g. github.com vs gitlab.com, or self-hosted instances) refers to distinct repositories.
Tracks `LastPolledAt` for per-repo poll timing.
Carries a Repository Eligibility status, re-evaluated on each poll cycle.

## Available Repository

A candidate shown in the add-repository picker — the listing that answers "which repos can *this* account monitor". Three orthogonal facts about a repo are distinguished and must not be conflated:

- **Visible** — the account's token can see the repo (it appears in the provider's repository listing at all).
- **Writable** — the token can push to it (`CanPush`). A visible-but-not-writable repo is shown disabled with a "no write access" reason, so the operator can rotate the PAT to unlock it rather than have it silently hidden.
- **Claimed** — the repo's owner namespace is covered by one of *this* account's Namespace Claims, tested with `Namespace.IsPrefixOf` (a claim on a parent namespace covers child paths, so a GitLab group claim covers its nested subgroups and projects).

The picker rule: the listing (`GET /api/accounts/{accountId}/repositories/available-repositories`) returns only repos that are both visible and claimed by the selected account. This makes the picker truthful — a repo it offers is one the account will actually resolve and serve at monitor time. Writable-but-unclaimed repos are excluded; claimed-but-read-only repos are shown disabled.

Each entry carries an `IsMonitored` flag, set when a Monitored Repository already exists for the same (Host, Repository Slug) pair. Monitored entries render a check mark, are non-selectable, and expose a screen-reader "already monitored" label — matched on Host + Slug, so the same slug monitored on a different host is not flagged. The monitored state wins over the read-only state: a repo that is both already monitored and non-writable shows the monitored check, not the "no write access" affordance.

An empty listing is disambiguated for the operator: an account with **no** Namespace Claims yields an explanatory "no claimed namespaces" state, distinct from a claimed-but-empty result and from a token/load failure — the response carries a `HasClaims` flag so the picker can tell these apart rather than showing a bare empty list.

Accepted asymmetry: a repo visible to account A but namespace-claimed by account B (whose own token cannot see it) is unaddable under A — A's listing excludes it because A does not claim its namespace, and B's listing excludes it because B's token cannot see it. This is intended: B is the account that would serve the repo at monitor time, and if B's token cannot reach it, monitoring would fail there anyway.

## Repository Priority

The relative ordering of monitored repositories, expressed as a 0-based, contiguous, unique `Position` integer on each Monitored Repository — lower value means higher priority.
`Position` is one component of Dispatch Order and therefore affects both dispatch and the dashboard's queued-issue list.
New repositories append at the end (highest existing Position + 1); deleting a repository renumbers survivors contiguously.
Polling is independent of position.

## Dispatch Order

The total order that governs which queued issue is claimed next and how queued issues are ranked in the dashboard.
Defined as the four-tuple `(TierRank, Position, DetectedAt, Id)`, evaluated in that precedence — the issue with the lexicographically smallest key is claimed first (and displayed first).

- **TierRank** — a property of the queued state variant that encodes dispatch-priority class: revision (0) < continuation (1) < fresh (2). Lower rank is claimed first; this is the primary ordering criterion.
- **Position** — the `EligibleRepository.Position` of the repository that owns the issue, supplied externally into the key. Lower position is preferred within a tier.
- **DetectedAt** — oldest first within the same repository and tier.
- **Id** — guarantees a deterministic total order when all other fields are equal.

A single shared definition (`DispatchOrderKey`) governs both the dispatcher (`WorkerCapacityAvailableHandler`, min-by-key claim selection) and the dashboard list query (`GetActiveIssueSummariesAsync`, in-memory sort of the queued subset), so display order and dispatch order cannot drift.

The dashboard partitions queued issues into two groups before applying the key: eligible-repository issues (real `Position`) rank above ineligible-repository issues (sentinel `int.MaxValue` position), each retaining its `RepositoryEligibilityStatus` for display.

## Repository Eligibility

Whether a Monitored Repository meets Foundry's processing preconditions (Branch Protection).
Modeled as a value object with three variants: `Eligible`, `Ineligible` (carries a non-empty collection of `EligibilityViolation` values), and `Unreachable` (the provider API could not be reached to perform the check — transient, retried each poll).
Stored on the Monitored Repository, evaluated synchronously at repository creation and re-evaluated on every poll cycle; a manual "re-check" action forces immediate re-evaluation.
Only `Eligible` repositories have their queued issues dispatched — ineligibility gates dispatch only; detection, dependency reconciliation, and review polling continue regardless. Already-running workers are unaffected.
Issue-level ineligibility is derived from the repository for display, never stored per issue.

## Eligibility Violation

A value object describing a specific, user-actionable Branch Protection precondition failure on a repository.
Carries `Rule` (a well-known string constant, e.g. `"branch-protection:allow-direct-pushes"`) and `Description` (human-readable explanation for dashboard display).
Stored as a non-empty collection on the `Ineligible` variant of Repository Eligibility, surfaced on the repository card in settings.
Provider-unreachable is modeled as the separate `Unreachable` eligibility variant rather than a violation, keeping the violation list strictly actionable.

## Issue Dependency

A directed "blocked by" relationship between two issues within the same repository.
Stored as a collection of blocking issue numbers on the Issue aggregate — the blocking issue may or may not be tracked by Foundry.
Both GitHub (REST API v2026-03-10) and GitLab (Issue Links API, Premium+) expose dependencies via structured APIs.
Foundry fetches dependencies during the detection poll cycle and reconciles them on each subsequent poll.
When the provider does not expose dependency links (e.g. GitLab Free tier), the provider degrades gracefully — it treats the issue as having no dependencies and logs once, rather than failing the poll.
Circular dependencies are detected by a domain service and flagged via a `CircularDependencyDetected` domain event.
A blocker is *resolved* when it is closed in the provider — the provider client filters out closed blockers at the anti-corruption boundary so domain logic never sees them.
The provider client also scopes "blocked by" links to the same repository or project at this boundary — GitHub by `repository.full_name`, GitLab by the linked issue's numeric `project_id` — so cross-project and cross-repository links never reach domain logic.
Link entries whose project or repository cannot be determined are kept as blockers (fail-safe toward blocking).
When a blocker's state is missing or unrecognized, it is treated as still blocking (fail-safe).

## Blocked Issue

A lifecycle state for an issue that has unresolved dependencies.
A `DetectedIssue` with blockers transitions to `BlockedIssue` instead of `QueuedIssue`.
A `QueuedIssue` that gains blockers is demoted to `BlockedIssue`.
When all blockers are resolved — that is, closed in the provider — a `BlockedIssue` transitions to `QueuedIssue`.

## Review Issue

A lifecycle state for an issue whose worker completed successfully and produced a PR.
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, and `FeedbackCutoffAt` — all non-nullable.
Awaits human review of the PR. The monitoring service polls the provider for PR/issue status and review feedback.
`FeedbackCutoffAt` filters stale feedback — only review comments submitted after this timestamp are considered actionable. Set to the worker run's completion time on first entry; updated on re-entry after a revision cycle.
Transitions: `Revise()` → `RevisionQueuedIssue` (feedback detected); `Complete()` → `CompletedIssue` (issue closed); `Fail()` → `ContinuableFailedIssue` (PR closed without merge — branch exists).

## Unchanged Issue

A lifecycle state for an issue whose worker completed successfully (exit code 0) but produced no code changes — no branch, no PR.
Requires manual resolution: the user can retry (disagreeing with the worker's assessment).
Classified as Active / Needs attention (see [Issue Lifecycle Partition](#issue-lifecycle-partition)) — it cannot resolve itself.
Transitions: `UnchangedIssue.Retry()` → `QueuedIssue`.
Hard-deleted when the provider-side issue is closed or loses its trigger label (untracked by the poller). If reopened upstream with the trigger label it is re-detected as a new issue.

## Revision Queued Issue

A lifecycle state for an issue queued for revision after receiving review feedback.
Carries `BranchName`, `PullRequestUrl`, and `ReviewComments` (`IReadOnlyList<ReviewComment>`) — all non-nullable.
Created from `ReviewIssue.Revise()` when the monitoring service detects a "changes requested" review.
Claimed with priority over regular `QueuedIssue` to minimize open issue count.
Transitions: `Claim()` → `RevisionInProgressIssue`.

## Revision In-Progress Issue

A lifecycle state for an issue whose worker is executing a revision cycle.
Carries `WorkerRunId`, `BranchName`, and `PullRequestUrl` — all non-nullable.
Created from `RevisionQueuedIssue.Claim()`.
Transitions: `MarkInReview()` → `ReviewIssue` (worker pushed changes); `MarkUnchanged()` → `ReviewIssue` (worker made no changes but PR still exists); `MarkFailed()` → `RevisionFailedIssue`.

## Completed Issue

Terminal lifecycle state — the issue is resolved via a merged PR.
Carries `BranchName`, `PullRequestUrl`, and `CompletedAt` — all non-nullable.
Created from `ReviewIssue.Complete()` when the provider-side issue is closed.
Preserved when the issue is subsequently untracked (completion wins over provider closure).

## Failed Issue

A lifecycle state for an issue whose fresh worker run failed without producing recoverable work (no branch pushed).
Also used when a PR is closed without merge and the review had no prior branch context.
Carries `WorkerRunId`, `FailureReason` (string description), and `FailedAt`.
Can come from `InProgressIssue` (worker failed before pushing a branch) or `ReviewIssue` (PR closed without merge, no branch recovery needed).
Transitions: `FailedIssue.Retry()` → `QueuedIssue` (fresh run, no branch context).

## Continuable Failed Issue

A lifecycle state for an issue whose worker run failed but left recoverable work on a pushed branch.
Carries `WorkerRunId`, `BranchName`, `FailureReason`, and `FailedAt` — all non-nullable.
Optionally carries `PullRequestUrl` — present when created from `ReviewIssue.Fail()` (PR was closed without merge), absent when created from `InProgressIssue` (no PR existed).
Created from `InProgressIssue` when the failed run left commits on the branch — Foundry checks via `HasBranchCommitsAsync` against the provider after the container exits.
Also created from `ReviewIssue.Fail()` since `ReviewIssue` always has a branch.
Retry dispatches a continuation run that checks out the existing branch and resumes implementation.
Transitions: `ContinuableFailedIssue.Retry()` → `ContinuationQueuedIssue` (resumes existing branch).

## Continuation Queued Issue

A lifecycle state for an issue queued for continuation from an existing branch with prior work.
Carries `BranchName`.
Created from `ContinuableFailedIssue.Retry()`.
Transitions: `Claim()` → `InProgressIssue` (reuses the existing in-progress variant; the continuation context lives in the dispatch payload).

## Continuation Context

The dispatch payload extension for continuation-aware worker execution.
Carries `BranchName` and optional `FailureReason`.
Present on `ClaimedIssueDispatch` when the claimed issue was a `ContinuationQueuedIssue`; absent for fresh attempts.
When `FailureReason` is set, it is surfaced (fenced as data) in the continuation section of the worker's system prompt so the worker has context on why the previous run failed.
Semantically distinct from `RevisionContext` — continuation resumes interrupted implementation, revision addresses review feedback on a completed PR.

## Revision Failed Issue

A lifecycle state for an issue whose revision worker run failed.
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, `FailureReason`, and `FailedAt` — all non-nullable.
Created from `RevisionInProgressIssue.MarkFailed()`.
Preserves branch context so retry re-enters the revision path.
Transitions: `Retry()` → `RevisionQueuedIssue` (re-enters revision path with existing branch).

## Operator-Triggered Retry

A manual action available on any retry-supporting issue via `POST /api/issues/{id}/retry`.
Dispatches polymorphically on the loaded issue state: `FailedIssue.Retry()` → `QueuedIssue` (fresh run); `ContinuableFailedIssue.Retry()` → `ContinuationQueuedIssue` (resumes existing branch); `RevisionFailedIssue.Retry()` → `RevisionQueuedIssue` (re-enters revision path); `UnchangedIssue.Retry()` → `QueuedIssue` (fresh run, operator disagrees with worker assessment).
Any non-retryable state returns a conflict error with no state change.

## Transient Retry

A bounded, automatic retry for issues that failed with `FailureReason.TransientApiError`, run by `TransientRetryService` (a periodic background service in the Issues module, 60 s tick).
Each tick, it loads `FailedIssue` / `ContinuableFailedIssue` candidates whose `failure_category` is `transient_api_error` (a SQL-filterable column) with a coarse `FailedAt <= now - InitialBackoff` prefilter, then decides per candidate in memory: the attempt count is the number of leading consecutive transient runs derived from the append-only `worker_runs` rows (`IWorkerRunQueries.CountConsecutiveTransientRunsAsync`, bounded at `MaxTransientRetries + 1` materialized rows), and the issue is due when `FailedAt + backoff(attempt)` has elapsed.
Due candidates are re-queued through the existing `Retry()` transition — `FailedIssue` → `QueuedIssue`, `ContinuableFailedIssue` → `ContinuationQueuedIssue` — so a de-labelled or already-retried issue is a safe no-op (a concurrent manual retry that moved the issue out of the failed state is tolerated).
Constants are hardcoded (no settings surface): `MaxTransientRetries = 2`, `InitialBackoff = 1 minute`. With `MaxTransientRetries = 2` both auto-retries use a flat 1-minute backoff — exponential doubling only applies if `MaxTransientRetries` is raised above 2 (the exhaustion guard fires before `ComputeBackoff` is reached for attempt ≥ 2). At 2 consecutive transient runs the issue is exhausted — the service stops retrying it, leaving `POST /api/issues/{id}/retry` as the manual escape hatch.
Due-ness is recomputed from the persisted `FailedAt` on every tick, so a host restart during a backoff window costs at most one extra tick of delay — no in-memory timer is required.

## Review Comment

A value object representing a single piece of reviewer feedback on a PR.
Carries `Body` (text), optional `FilePath`, and optional `Line` number.
Lives in Issues contracts — produced by the monitoring provider, consumed by the worker dispatch.

## Review Feedback

A provider-agnostic result from `IIssueProvider.GetReviewFeedbackAsync()`.
Carries an `IReadOnlyList<ReviewComment>`.
Each provider implementation decides what constitutes actionable feedback — GitHub uses `CHANGES_REQUESTED` reviews; GitLab uses unresolved discussion threads (tier-independent, unlike the Premium-only "Request changes" reviewer state).

## Revision Context

The dispatch payload extension for revision-aware worker execution.
Carries `BranchName`, `PullRequestUrl`, and `IReadOnlyList<ReviewComment>`.
Present on `ClaimedIssueDispatch` when the claimed issue was a `RevisionQueuedIssue`; absent for fresh attempts.
The worker uses this to check out the existing branch and address the specific review comments.

## Trigger Label

The constant label `foundry` applied to a provider-side issue to flag it for Foundry processing.
The label stays on the issue untouched — Foundry does not add, remove, or swap labels on the provider.
All lifecycle state is tracked internally in the database.

## Provider as Source of Truth for Issue Closure

The provider is the authoritative signal for issue closure.
When an issue's trigger label is removed or the issue is closed on the provider, it disappears from the `?labels=foundry&state=open` fetch.
On each poll cycle, the poller emits a `ProviderIssueUntracked` integration event for any tracked issue absent from that fetch.
The `ProviderIssueUntrackedHandler` hard-deletes tracked records in resting states: `detected`, `queued`, `blocked`, `failed`, `continuable_failed`, `revision_failed`, `revision_queued`, `continuation_queued`, and `unchanged`.
`completed` is preserved — completion wins over provider closure.
`in_progress`, `revision_in_progress`, and `review` are preserved — a live worker is running or the issue is under active review; worker cancellation is out of scope.
An issue closed on the provider and later reopened with the trigger label is re-detected as a new issue.

## Provider Issue

A DTO representing an issue as returned by a provider's API.
Carries raw data (number, title, body, author username, URL, labels) that the domain maps into value objects and aggregates during detection.

## Repository Slug

The full namespace path that identifies a repository within a provider — two or more `/`-separated segments (`owner/name` on GitHub, `group/subgroup/project` for nested GitLab groups).
Value object — `Name` is the last segment, `Owner` is everything before it (a multi-segment namespace path for GitLab). The provider adapter renders it as needed: GitHub as `owner/name` path segments, GitLab as a URL-encoded full path.

## WorkerRun

A single execution of a worker against an issue — the primary aggregate for the Workers module.
Modeled as a polymorphic aggregate with state variants: `StartingRun` (container creation requested), `ActiveRun` (container running), `CompletedRun` (exited successfully), `FailedRun` (exited with error, timed out, or failed to start).
An issue can have multiple runs (e.g. after retry from Failed or Review state).
Foundry derives each run's outcome via `WorkerOutcomeResolver` — a pure function that consults merge-request state first (see Worker Outcome Detection).
`ActiveRun` carries the `BranchName` — set at activation from the dispatch payload's pre-created branch name, propagated to `FailedRun` and `CompletedRun`.

## Worker Outcome Detection

How Foundry establishes what a worker accomplished, resolved by `WorkerOutcomeResolver` — a pure, side-effect-free function applied after the container exits (and on timeout, orphan-reconcile, and container-not-found paths).

**MR-state-first lookup.**
The resolver queries `GetMergeRequestByBranchAsync`, keyed on the run's stored `BranchName` (which survives remote branch deletion).
The resulting presence maps to outcomes:

- `Merged` → `Completed` (transitions `InProgressIssue` → `CompletedIssue`)
- `Open` → `Review` (transitions `InProgressIssue` → `ReviewIssue`)
- `Closed` (unmerged) + branch has commits → `ContinuableFailure`; otherwise → `Failure`
- `None` (no MR found) → fall back to exit-code + branch-commits path (see below)

**NotFound vs transient errors.**
Provider query failures carry `Error.Kind`.
Only `ErrorKind.NotFound` is a definitive signal — "branch deleted, no commits reachable".
Any other failure (`Unknown`, network error, timeout) yields `Indeterminate` — no state transition, the run stays active and is retried on the next tick.

**No-MR fallback.**
When no MR exists the resolver falls back to exit code and branch commits.
Exit 0 with commits triggers a bounded MR re-poll (preserving the PR-race retry window); exit 0 with no commits → `Unchanged` or `UsageLimited` (detected via JSON output); non-zero exit → `ContinuableFailure` (commits exist) or `Failure`, with bootstrap-failure and usage-limit classification layered on top.

**Side-effect applier.**
A single `ApplyOutcomeAsync` performs all side effects in one place: state transition, integration-event dispatch (carrying `WorkerRunMergeState`), usage-limit persistence + dispatch-pause, and container stop-and-remove.
Every terminal outcome (`Completed`, `Review`, `ContinuableFailure`, `Failure`, `Unchanged`) stops and removes the container — this is an invariant enforced at the applier, not a per-path decision.
`Indeterminate` → log and return; container left running for the next tick.

**Timeout watchdog.**
The watchdog ceiling (`StartedAt + timeout_minutes`) is evaluated regardless of whether the container is still running.
Any unresolved run that exceeds the ceiling is processed through the same resolver → applier pair (exit code null; MR-state still consulted first), so no run can hold a worker slot indefinitely.

**Docker daemon reachability.**
`GetStatusAsync` returns a discriminated `WorkerStatusProbe` distinguishing three cases:

- `Available` — container exists (running or exited); carries a `WorkerStatus` with `IsRunning`, `ExitCode`, and `FinishedAt`.
- `NotFound` — container is definitively gone (Docker 404).
- `Unreachable` — daemon connectivity failure: only `HttpRequestException`, `TimeoutException`, or a self-timeout `OperationCanceledException` (caller's token was NOT already cancelled).
  Any other `DockerApiException` propagates as an unexpected error.

On `Unreachable` the active run is left in `ActiveRun` state (never failed) and recovers on a later tick.
Startup reconciliation is deferred when the daemon is unreachable — the `_reconciled` latch is gated on daemon reachability across both the orphan-container sweep (`ListByLabelAsync`) and per-run `GetStatusAsync` probes — so the orphan sweep is not silently skipped for the boot.

A `/ready` readiness health check (tagged `ready`, mapped unconditionally at `/ready`) surfaces Docker daemon connectivity:
healthy when the daemon responds to a ping within 3 seconds, unhealthy with a message identifying Docker daemon connectivity when the ping fails or times out.

## Container Output

The captured tail of a failed worker container's stdout/stderr, stored on `FailedRun` as a nullable string.
Captured by Foundry (not the worker) from the Docker API after the container stops but before removal.
Best-effort — null when the container is already gone, never started, or the Docker API call fails.
Docker timestamps are enabled (`--timestamps` flag) so each output line carries an RFC 3339 prefix, providing a unified timeline.

Failed-run output is served by `GET /api/workers/runs/{workerRunId}/log` (200 with text body when available, 204 when absent, 404 when the run is unknown) and rendered in the dashboard by the shared `fd-log-view` component (static source mode) in the issue detail panel.

Running workers stream live output via the `WorkerHub.StreamLog(workerRunId)` method on `/hubs/workers` — the hub method returns an `IAsyncEnumerable<string>` of redacted log lines with backlog replay followed by live follow.
The dashboard's `fd-log-view` component subscribes to this stream (stream source mode) and interleaves commit markers by timestamp to give a unified activity timeline.

## First-Run Wizard

A guided setup flow (`/setup`) that runs when no accounts are configured.
Three steps: select auth mode (API key or OAuth), add first account (provider, base URL, PAT — the account name is derived from the token identity), select repositories to monitor (fetched from provider API).
Auto-redirects from `/issues` when no accounts exist. Redirects to `/issues` on completion.
The wizard reuses the same form components as the settings page.

## FailureReason

A value object on FailedRun that classifies how the run failed.
Variants: `NonZeroExit(exitCode)` (container exited with non-zero code), `TimedOut` (exceeded configured timeout), `ContainerError(message)` (Docker-level failure — image not found, daemon unavailable, etc.), `UsageLimited(resetsAt)` (worker hit a *time-based* Anthropic API usage limit — session, weekly, or Opus quota — carrying a parseable reset time; token `usage_limited`), `CreditsExhausted` (worker hit a *money-based* 429 — credit pool empty, org monthly spend limit reached, or the CLI ≥ 2.1.119 regression — carrying no reset time; token `credits_exhausted`; summary `"Credits exhausted"`, which deliberately shares no prefix with the usage-limit summary so the auto-resume `LIKE 'Usage limit reached%'` sweep cannot catch it; blocks the account's spend indefinitely until an operator resumes), `WorkerBootstrapFailed(detail)` (pre-task failure — the worker container died during entrypoint bootstrap, before `claude` ran; carries a short, secret-redacted diagnostic `Detail` with the failed stage and error tail), `AuthInvalid` (the worker's Claude credentials were rejected — `api_error_status == 401` or `error.type == "authentication_error"`; triggers the auth-invalid pause), `TransientApiError` (a transient Anthropic API fault the run neither caused nor can fix — `api_error_status` in the 5xx range, or `is_error: true` with `api_error_status: null` and a `result` matching a known transient phrase such as a mid-response connection drop or `529 Overloaded`; token `transient_api_error`; drives the bounded auto-retry described under Transient Retry), `ProviderError(message)` (a provider API call needed to start the run failed, e.g. branch pre-creation rejected with 403; raised before the worker task begins).

## Usage Limit

A *time-based* state where the Anthropic API quota (session, weekly, or Opus limit) is exhausted and self-heals at a known reset time.
Detected by parsing the worker container's JSON output (`--output-format json`): the primary signal is `ResultMessage.api_error_status == 429`; the `terminal_reason` allowlist (`"blocking_limit"`, `"rapid_refill_breaker"`) is retained as a secondary signal for older output shapes. Note that a 429 can arrive with `subtype: "success"` and `terminal_reason: "completed"`, so neither field is reliable on its own.
The reset time is extracted from the human-readable result text. Two wall-clock wording shapes are handled: `resets <time> (UTC)` (e.g. `"You've hit your limit · resets 12:10am (UTC)"`) and `reset at <time> (<IANA zone>)` (e.g. `"Your limit will reset at 3pm (America/New_York)."`). In both forms the time is 12-hour; a bare hour without minutes (e.g. `3pm`) defaults minutes to `00`. UTC times resolve to the next future UTC occurrence directly; IANA zone names are converted to UTC via the system timezone database, then the same roll-forward applies. ISO-8601 timestamps in the result text take precedence over wall-clock parsing.
Whether a reset time parses is the discriminator between the two 429 classes (see ADR 0046): a 429 with a parseable reset time is a `UsageLimited` time-based limit; a 429 with no parseable reset time is a money-based block classified as `CreditsExhausted` (see FailureReason and Dispatch Pause). There is no fabricated-cooldown fallback — the former `DefaultCooldownMinutes` setting is removed.
A detected usage limit always triggers a global dispatch pause via `GlobalSettings.UsageLimitResetsAt` — there is no immediate-requeue path. `GlobalSettings.SetUsageLimitResetsAt` remains extend-only and clamps to 7 days, so a reset time already in the past only ever extends an existing pause.
The triggering issue transitions to `FailedIssue` / `ContinuableFailedIssue` with `FailureReason.UsageLimited(resetsAt)`.
On detection, `WorkerDispatchService` raises the `DispatchPaused` integration event, which is broadcast as a `dispatch` system notification (`isActive: true`) so the dashboard usage-limit banner updates live without a page refresh.

## Dispatch Pause

A global operational state where the dispatch loop skips issuing new work.
Two independent global triggers on `GlobalSettings`: `UsageLimitResetsAt` (automatic, from usage limit detection) and `IsDispatchPaused` (manual, from operator "Pause All" action).
Dispatch is paused when either is active.
Auto-resume clears `UsageLimitResetsAt` and retries all `FailureReason.UsageLimited` issues when `AutoResumeOnUsageReset` is enabled.
Manual "Resume All" clears both flags and retries usage-limited issues.

A third, independent pause is the **credit block**: when a run fails with `CreditsExhausted`, the `ClaudeAccount` aggregate's `SpendState` transitions to `Blocked`, and `CredentialGate.CanDispatchAsync` then refuses dispatch regardless of the global flags. This pause is *indefinite* — a money-based block carries no reset time, so nothing auto-clears it; only an operator "Resume All" clears it (the follow-up issue adds a credit probe and auto-resume). `SpendState` and `CredentialValidity` are independent: an account can be simultaneously credit-blocked and auth-invalid, the gate refuses on either, and clearing one leaves the other in force. Manual "Resume All" publishes `DispatchResumed`; the Credentials module reacts by calling `RestoreSpend()` and — only when the state actually changed — publishing `CreditsRestored`, on which the Issues module re-queues every issue whose `FailureReason` equals `"Credits exhausted"` (an exact match, never a prefix sweep).
Already-running workers are unaffected — only queued issues are held.
Both pause and resume broadcast a `dispatch` system notification (`isActive: true` on pause, `isActive: false` on resume); the client treats this as a pure reload trigger and re-syncs banner state from `/api/settings` (the authoritative source) rather than reading any timestamp off the SignalR payload.

## System Notification

A lightweight SignalR broadcast (`Category`, `IsActive`, `Message`) delivered to all dashboard clients via the system hub, used for global operational banners.
Categories in use: `dispatch`, `docker`, `image-build`, `auth`, `license`.
Client semantics: an `isActive: true` notification is stored, replacing any prior entry for its category; `isActive: false` removes the category's entry.
Both `dispatch` and `image-build` are pure reload triggers — each broadcast carries an empty message and signals the client to re-sync authoritative state from `/api/settings` rather than reading any payload (see Dispatch Pause and Worker Image Build State).
Ephemeral — broadcast directly, never through the transactional outbox, since there is no durable consumer.

## Container Output Parser

An infrastructure service (`IContainerOutputParser`) that classifies a worker container's JSON output.
Takes raw JSON from `--output-format json` and returns a discriminated result: `NormalExit`, `UsageLimited(DateTimeOffset ResetsAt)`, `CreditsExhausted`, `AuthInvalid`, `TransientApiError`, `ParseFailure(string RawOutput)`, `NoResultLine`, or `WorkerBootstrapFailed(string Detail)`.
Inspects `ResultMessage.api_error_status` (429) as the primary limit signal, with the `terminal_reason` allowlist as a secondary signal. When a limit signal is present, reset-time parse **success** is the discriminator (see ADR 0046): if a reset time is extracted from the result text via best-effort regex (wall-clock or ISO-8601) the result is `UsageLimited(resetsAt)`; if none parses the result is `CreditsExhausted`. `Parse` takes no cooldown fallback parameter — the former fabricated `DefaultCooldownMinutes` reset time is removed.
Auth failures are detected from `api_error_status == 401`, with `error.type == "authentication_error"` as a secondary guard.
Transient API faults are detected from `api_error_status` in the 5xx range (the durable signal), or — when `api_error_status` is null and `is_error: true` — from a narrow frozen allow-list of transient `result` phrases (`"API Error: Connection closed mid-response"`, `"API Error: 529 Overloaded"`) matched case-sensitively as a substring. The predicate is status-5xx-or-allow-list, never a bare `is_error: true`, so genuine task failures are never laundered into a retryable category. When `is_error: true` matches no known category (usage-limit, auth, transient), the parser logs a warning naming the unclassified `result` text — a tripwire for a reworded transient phrase that the brittle allow-list would otherwise miss silently.
Bootstrap failures are detected via a sentinel line (`FOUNDRY_BOOTSTRAP_FAILED stage=… detail=…`) emitted by `entrypoint.sh` when the container dies before `claude` runs (clone, auth, or branch stage); the parser scans for the sentinel only when no Claude JSON result line is present, so a genuine result always wins; a non-zero exit with no result line and no sentinel falls back to `WorkerBootstrapFailed` heuristically.
Domain types remain JSON-unaware — all parsing is in infrastructure.
