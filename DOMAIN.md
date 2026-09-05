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

## Provider Rate Budget

The finite pool of API requests a provider grants per window.
A per-provider, per-budget concept tracked independently for each budget key.

| Budget | Limit |
|---|---|
| GitHub REST, authenticated user | 5,000 req/hour |
| GitHub GraphQL, authenticated user | 5,000 points/hour (separate budget; cost = requests/100, min 1) |
| GitHub content creation (secondary) | 80/min and 500/hour |
| GitHub per-endpoint points (secondary) | 900/min REST, 2,000/min GraphQL; GET = 1, POST/PATCH/PUT/DELETE = 5 |
| GitHub concurrency (secondary) | 100 in flight |
| gitlab.com authenticated API | 2,000 req/min |
| Conditional GET returning 304 | does not count against the GitHub primary limit |

### Rate Budget Reading

The last observed `remaining` for one budget, together with its `limit`, `resetAt`, and `ObservedAt` (the wall-clock time Foundry recorded the reading).
Readings are in-memory only — they are not persisted across restarts.
A reading whose `ObservedAt` age exceeds the staleness window is treated as stale.

### Provider Budget Health

A three-valued verdict produced by the Provider Budget Policy from a reading and a floor:

- **`Healthy`** — a fresh reading with `remaining >= floor`.
- **`Low`** — a fresh reading with `remaining < floor`. Logged once on transition; surfaced on the dashboard. Polling continues — the governor does not block.
- **`Unknown`** — the reading is absent or stale. Never treated as exhaustion (fail open).

GitHub REST and GitHub GraphQL produce independent verdicts; a `Low` on one does not gate the other.
GitLab headroom is recorded for visibility but produces no verdict (not evaluated against a floor).

### Fixed Per-Cycle Cost

The count of provider API calls a poll cycle issues that does **not** scale with issue count.
Bounded by `RepositoryPoller.MaxFixedPollCallsPerCycle`.
ADR 0066 and this glossary reference this constant by name rather than restating its value — there is one place to change it and the change is reviewed at the constant.
A compile-time invariance test asserts that driving `RepositoryPoller.PollAsync` with 5 issues and with 200 issues produces identical total provider-call counts, and that the fixed-cost scenario stays within `MaxFixedPollCallsPerCycle`.

The governor's enforcement is observability and early warning; the compile-time invariance test is the actual enforcement layer.
See [ADR 0066](docs/adr/0066-provider-api-request-budget-governor.md).

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

### Account Identity

An account is identified by `(PAT Owner, derived owner-namespace set, host)`.
The login alone is not enough, because one login legitimately holds several tokens — GitHub fine-grained PATs are each bound to a single resource owner, so reaching two owners requires two tokens and therefore two accounts.
A request is a **duplicate account** when another credential on the same host carries the same `Credential.Name` *and* its Namespace Claims cover the *entire* incoming token's derived owner namespace set — leaving no unclaimed namespace the new account could exclusively hold.
Overlap is tested on exact namespace values.
Both the create and update paths apply the same never-steal semantics: same-login sibling namespaces are subtracted from the conflict set and never offered for transfer.
On create, a partial overlap with a same-login sibling is not a duplicate — the account is created claiming only the unclaimed namespaces; the sibling's namespaces are silently skipped.
On a token-bearing update the same qualifier applies: the rotation is rejected only when the same-login sibling covers the entire derived set, leaving the rotated account with nothing of its own.
A partial overlap on update reduces to the account's own namespaces via the never-steal subtraction, so a token spanning both owners still rotates cleanly.
Same login with disjoint owners is permitted — that is the normal shape, not an exception.
A *different* login reaching an already-claimed owner is not a duplicate; it is a Namespace Claim conflict, resolved through takeover.
Detection is server-side only, in the create and update handlers: the derived owner set requires a provider repository listing, so no client can compute it.
On a token-bearing update, derivation runs and both guards evaluate *before* the credential is mutated, so a rejected rotation persists nothing; a derivation that returns `Unavailable` rejects the update rather than proceeding unguarded (see [ADR 0064](docs/adr/0064-account-identity-keyed-on-login-owner-set-and-host.md)).

### Token Validation Outcome

A closed variant set (`TokenValidationOutcome`) returned by both provider adapters after validating a PAT.
Five variants:

- `Authenticated(AccountName, MissingScopes)` — the token authenticated and the PAT owner resolved; `MissingScopes` lists any required scopes absent from the token.
- `AuthenticationFailed` — the token was rejected (401/unauthorized).
- `ScopesUnverifiable(AccountName)` — the token authenticated and the owner resolved, but the token's scopes could not be read. Triggered by: GitLab group/project access tokens where `personal_access_tokens/self` returns non-2xx; and GitHub tokens served without an `X-OAuth-Scopes` response header that are **not** fine-grained PATs (e.g. GHES classic tokens, server configurations that suppress the header). GitHub fine-grained PATs (`github_pat_` prefix) are classified by prefix at validation time and, when served without `X-OAuth-Scopes`, return `Authenticated` with empty `MissingScopes` instead — no caution. Fine-grained permission enforcement is delegated to the provider and the create-time write probe (`IGitHubWriteProber`); see [ADR 0053](docs/adr/0053-github-token-type-by-prefix.md).
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
Includes worker settings (max concurrent, timeout, prompt templates), authentication mode (API key or OAuth), the Worker Image Configuration and Image Build State, the credit-probe interval (`ProbeIntervalMinutes`, default 60, validated to a minimum of 5), and dispatch pause controls: usage-limit controls (`AutoResumeOnUsageReset`, `UsageLimitResetsAt`) and the auth-invalid pause (`IsAuthInvalidPaused`) — both pause dispatch until explicitly resumed, but only the usage-limit pause supports auto-resume. `IsDispatchPaused` is the separate manual operator pause.
`ProbeIntervalMinutes` governs how long the credit block waits between probes (see Credit Probe); the operator sets it in the dispatch-settings form, `GlobalSettings.UpdateProbeInterval` rejects a below-minimum value, and the credit-probe scheduler reads it via `IGlobalSettingsQueries.GetProbeIntervalMinutesAsync` at each arm.
DB is the single source of truth — `IConfiguration` is not consulted for settings the UI manages.
Infrastructure-only settings (Docker image, mounts, memory/CPU/PID limits) remain in `IConfiguration`.

## Monitor

The background process that polls configured repositories for issues labeled `foundry`.
Runs on a fixed tick interval (30s default) and checks whether each repo is due for polling based on its configured poll interval and `LastPolledAt` timestamp.
The per-cycle provider call cost is bounded by `RepositoryPoller.MaxFixedPollCallsPerCycle` (see [Provider Rate Budget](#provider-rate-budget)) and enforced by the poll-call invariance test.

## Issue

A provider-side issue tagged for Foundry processing.
Modeled as a polymorphic aggregate — each lifecycle state is a distinct type (`DetectedIssue`, `BlockedIssue`, `FreshQueuedIssue`, `ContinuationQueuedIssue`, `RevisionQueuedIssue`, `InProgressIssue`, `RevisionInProgressIssue`, `ReviewIssue`, `UnchangedIssue`, `CompletedIssue`, `FailedIssue`, `ContinuableFailedIssue`, `RevisionFailedIssue`).
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
- `blocked` is Waiting because a `BlockedIssue` auto-transitions to `FreshQueuedIssue` when its blockers close on the provider; no user action is required.

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
`Position` is one component of Dispatch Order and therefore affects both dispatch and the order the dashboard's queued-issue list is served in.
New repositories append at the end (highest existing Position + 1); deleting a repository renumbers survivors contiguously.
Polling is independent of position.

## Dispatch Order

The total order that governs which queued issue is claimed next and the order the dashboard list query serves queued issues in.
Defined as the four-tuple `(TierRank, Position, DetectedAt, Id)`, evaluated in that precedence — the issue with the lexicographically smallest key is claimed first, and is served first by the dashboard list query.

- **TierRank** — a property of the queued state variant that encodes dispatch-priority class: revision (0) < continuation (1) < fresh (2). Lower rank is claimed first; this is the primary ordering criterion.
- **Position** — the `EligibleRepository.Position` of the repository that owns the issue, supplied externally into the key. Lower position is preferred within a tier.
- **DetectedAt** — oldest first within the same repository and tier.
- **Id** — guarantees a deterministic total order when all other fields are equal.

A single shared definition (`DispatchOrderKey`) governs both the dispatcher (`WorkerCapacityAvailableHandler`, min-by-key claim selection) and the dashboard list query (`GetActiveIssueSummariesAsync`, in-memory sort of the queued subset), so the two cannot disagree about the order at the moment the list is served.

The dashboard treats the server array order as the only source of queue order and reconciles it by refetching `GET /api/issues`, rather than deriving order locally or receiving a pushed order (see [ADR 0067](docs/adr/0067-dashboard-reconciles-queue-order-rather-than-deriving-it.md)).
Every `IssueUpdated` event schedules a debounced reconcile (300 ms) alongside the counts refetch that already runs on that path, collapsing a burst of events into one request.
A low-frequency safety-net timer (30 s) schedules the same reconcile, covering order inputs that raise no issue event — including a repository `Position` change in Settings.
The reconcile fetch carries a latest-wins request token, so a late response cannot install a stale order.
A failed reconcile sets a `queueOrderStale` signal to disclose bounded staleness without showing the blocking load-error banner; the next successful reconcile clears it.
The automatic exit from the possibly-stale state is the next event-driven or timer-driven reconcile, plus the full reload on SignalR reconnect.

The dashboard partitions queued issues into two groups before applying the key: eligible-repository issues (real `Position`) rank above ineligible-repository issues (sentinel `int.MaxValue` position), each retaining its `RepositoryEligibilityStatus` for display.

## Queue Position

A queued issue's 1-based index in Dispatch Order, counted over **dispatchable queued issues only** — queued-tier issues whose repository is eligible (not `ineligible` or `unreachable`).

Queue Position is derived from the rendered dispatchable-queued array on the dashboard (see [ADR 0067](docs/adr/0067-dashboard-reconciles-queue-order-rather-than-deriving-it.md)).
It is never persisted and never transported from the server; the ordinal indexes the very array the cards are rendered from, so it cannot disagree with the on-screen sequence.

Not-dispatchable queued issues — those whose repository is ineligible or unreachable — have no Queue Position.
Their card gutter shows `—` in place of an ordinal.

Queue Position 1 identifies the **Next up** issue: the one the dispatcher claims next when a worker slot becomes available.

## Dispatch Context

The sealed union describing the nature of the work being dispatched to a worker — assembled on the aggregate, not in the handler.
Three variants:

- **`Fresh`** — a new implementation attempt; carries `BranchName`.
- **`Revision`** — a revision cycle addressing review feedback; carries `BranchName`, `PullRequestUrl`, and `IReadOnlyList<ReviewComment>`.
- **`Continuation`** — resuming an existing branch after a failed run; carries `BranchName` and optional `FailureReason`.

`DispatchContext` is assembled by the concrete `QueuedIssue` subtype overriding the abstract `Context` property — `FreshQueuedIssue` returns `Fresh`, `RevisionQueuedIssue` returns `Revision`, `ContinuationQueuedIssue` returns `Continuation`.
The handler and claimer treat `Context` as an opaque value — they forward it into `ClaimedIssueDispatch` without switching on it.
Survives the outbox round-trip via `[JsonPolymorphic]` / `[JsonDerivedType]` annotations (see [ADR 0051](docs/adr/0051-dispatch-context-union-and-typed-dispatch-seam.md)).

## Claim

The act of assigning a queued issue to a specific worker run — the transition from a queued state variant to an in-progress one, paired with the `IssueClaimed` integration event that tells the Workers module to start a container.

A claim is authorized by exactly one `WorkerCapacityAvailable` event, which carries only a `WorkerRunId`.
Each event is backed by a durable [Dispatch Reservation](#dispatch-reservation) created atomically with the event; the reservation counts against slot occupancy from the moment it is written, preventing additional events from being published until the slot is consumed or released.
Before selecting a candidate, the handler checks whether any issue already carries the event's `WorkerRunId` — if so, the claim is skipped without selecting, preventing double-claims on redelivery (the at-least-once outbox guarantee means the same event may arrive more than once after a crash between claim and inbox commit).
An event that finds no candidate publishes `Claim Skipped`, which releases the reservation.

Claiming proceeds in three steps:

1. **Resolve eligible repositories** — every repository owning a queued issue is checked against Repository Eligibility, and eligible ones contribute their `Position`. Resolving all repositories up front (rather than checking the head candidate) means an ineligible repository cannot block dispatch of issues behind it.
2. **Select the winner** — the claimable issues across all eligible repositories and all three tiers are sorted by Dispatch Order key; the head of the sorted list whose repository has resolvable dispatch info wins. If the best candidate's repository cannot yield dispatch info, that candidate is skipped and the next-best is tried — the tick is NOT aborted. Dispatch info resolution is memoized per repository within a tick, so a second candidate from the same repository reuses the cached result without a second query.
3. **Claim it** — call `Claim(workerRunId)` on the aggregate, publish `IssueClaimed` carrying a `ClaimedIssueDispatch` (assembled from the winning candidate's pre-fetched dispatch info and the aggregate's own `DispatchBranchName` and `Context`), and transition the aggregate. The event and the state change commit as one atomic unit.

Claiming is the only path from a queued state to an in-progress one; nothing else in the system claims an issue. Claims are never concurrent — the outbox relay is a single host-level service that delivers sequentially.

**Terminal outcomes** are logged once at the handler: `Debug` when no eligible repositories exist or no candidates remain after eligibility filtering, `Warning` when every candidate was skipped because its repository's dispatch info could not be resolved (`AllCandidatesUnresolvable`). The "no covering credential" race is narrow — ineligibility already filters out repositories with no account in step 1, so an unresolvable dispatch info arises only when a credential is deleted or its token cleared between the last poll cycle and the claim.

## Dispatch Reservation

A durable record that holds a dispatch slot from the moment `WorkerCapacityAvailable` is published until the slot is consumed by a successful claim or released by a skip or sweep.
Stored in `dispatch_reservations` as an aggregate with `Id = WorkerRunId` and `ReservedAt`.
Created atomically with the `WorkerCapacityAvailable` outbox row inside `WorkerDispatchService`; the two writes share one transaction so the reservation can never exist without its corresponding event, and vice versa.

**Lifecycle:**

- **Reserve** — `WorkerDispatchService` creates one `DispatchReservation` per `WorkerCapacityAvailable` event it publishes.
- **Consume** — `IssueClaimedHandler` deletes the reservation for the claimed `WorkerRunId` in the same save as the new `StartingRun` row — one atomic transaction.
- **Release on skip** — `ClaimSkippedHandler` deletes the reservation when the Issues module publishes `Claim Skipped` (no eligible candidate found).
- **Release on sweep** — `StaleReservationService` deletes reservations whose `ReservedAt` is older than the stale threshold (2 minutes).
The sweep runs independently of `StaleStartingRunService` and has no Docker dependency, so a Docker outage cannot prevent stale reservations from being cleared (see [ADR 0069](docs/adr/0069-durable-dispatch-reservation.md)).

A reservation counts against [Worker Slot Occupancy](#worker-slot-occupancy) from the moment it is persisted, so the dispatch gate sees a full slot even before the worker container starts.

## Claim Skipped

An integration event published by the Issues module (`WorkerCapacityAvailableHandler`) when a `WorkerCapacityAvailable` event is delivered but no eligible candidate can be claimed.
Carries `WorkerRunId` — the same id that was reserved and is now free to reuse.

Three non-`Selected` outcomes trigger `Claim Skipped`:

- `NoEligibleRepositories` — no repository that owns a queued issue passes eligibility checks.
- `NoCandidates` — repositories are eligible but no queued issue exists among them.
- `AllCandidatesUnresolvable` — every candidate's repository lacks resolvable dispatch info.

`ClaimSkippedHandler` (Workers module) deletes the matching `DispatchReservation` on receipt, freeing the slot for the next dispatch tick.
Redelivery is idempotent — if the reservation was already deleted (by the sweep or a prior delivery), the handler is a no-op.

## Queued Issue

The abstract intermediate in the Issue hierarchy covering all three queued state variants: `FreshQueuedIssue`, `RevisionQueuedIssue`, and `ContinuationQueuedIssue`.
Defined as `abstract QueuedIssue : Issue`, it carries the members shared by every claimable state:

- **`TierRank`** (abstract, computed) — the dispatch-priority rank, overridden by each concrete variant.
- **`DispatchBranchName`** (abstract, computed) — the branch name the worker should operate on.
- **`Context`** (abstract, computed) — the `DispatchContext` union value describing the nature of the work.
- **`Claim(Guid workerRunId)`** (abstract) — transitions the aggregate to its in-progress state with covariant return type on each override.

`QueuedIssue` collapses every three-way type union in the codebase — `is QueuedIssue` and `OfType<QueuedIssue>()` replace the former per-tier switches in `IsRestingState`, `IsQueuedVariant`, `GetUntrackableIssueNumbersAsync`, and `DispatchOrderKey.For`.

**EF Core registration.** EF Core 10 omits an abstract intermediate from the model unless it is explicitly registered — otherwise `OfType<QueuedIssue>()` and `is QueuedIssue` in translated queries throw `InvalidOperationException` at query time (not at model build or compile time).
`QueuedIssue` is registered via a dedicated `IEntityTypeConfiguration<QueuedIssue>` with `HasBaseType<Issue>()`.
No `HasValue<T>()` discriminator entry is added — EF assigns an unused default and `HasDiscriminator(...).IsComplete(true)` remains valid through the concrete leaves.
Computed get-only members (`TierRank`, `DispatchBranchName`, `Context`) are not mapped and require no `Ignore()`.

`OrderBy(TierRank)` does not translate to SQL — it refers to a computed, unmapped property. Sorting over the bounded queued set stays in memory (see [ADR 0025](docs/adr/0025-shared-dispatch-order.md)).

## Repository Eligibility

Whether a Monitored Repository meets Foundry's processing preconditions (Branch Protection and write permission).
Modeled as a value object with three variants: `Eligible`, `Ineligible` (carries a non-empty collection of `EligibilityViolation` values), and `Unreachable` (an eligibility input could not be established).
The write-probe sizing constraint (see [Provider Rate Budget](#provider-rate-budget) and [ADR 0054](docs/adr/0054-split-eligibility-cadence.md)) limits the number of repositories that can self-heal concurrently within the GitHub secondary rate limit.
`Unreachable` carries an `UnreachableReason` that distinguishes three causes — each with a distinct operator-facing message:

- **`NeverProbed`** — the write-probe result is `Unknown(Transport)`: no probe has succeeded yet, or the probe failed for a non-rate-limit reason.
- **`RateLimited`** — the write-probe result is `Unknown(RateLimited)`: the probe returned a GitHub 403 classified as rate-limit exhaustion (see below). The repository self-heals automatically when the REST budget resets; no operator action is needed.
- **`BranchRulesUnavailable`** — the branch-rules GET failed on the current poll cycle.

The `NeverProbed` and `RateLimited` causes recover via the `Unknown` self-heal cadence — the repository is automatically re-probed on the next poll cycle once a 15-minute cooldown has elapsed since the last attempt. `BranchRulesUnavailable` is retried on the next poll cycle unconditionally. `Granted` and `Denied` verdicts remain event-triggered.
Stored on the Monitored Repository and composed from two checks of different cadence:

- **Branch-rules GET (per-cycle)** — re-evaluated unconditionally on every poll cycle and synchronously at repository creation. A configuration change on the provider is reflected on the next poll without user action (auto-heal). A failed GET produces `Unreachable(BranchRulesUnavailable)`.
- **Write probe (event-triggered for Granted/Denied; self-healing for Unknown)** — runs on repository add, manual re-check, and credential update/rotation. The last result is persisted on `MonitoredRepository` as a `WriteProbeVerdict` value object (`Granted` / `Denied` / `Unknown`), which carries an `UnknownReason` (`Transport` or `RateLimited`). The verdict is composed with the fresh branch-rules result each cycle. `Unknown(Transport)` maps to `Unreachable(NeverProbed)`; `Unknown(RateLimited)` maps to `Unreachable(RateLimited)`. Both are automatically re-probed after a 15-minute cooldown; `Granted` and `Denied` are only re-probed by operator or credential events. A failed probe stamps `Unknown.LastAttemptedAt` so the next automatic retry is one cooldown away. See [ADR 0054](docs/adr/0054-split-eligibility-cadence.md) and [ADR 0055](docs/adr/0055-distinguish-unreachable-eligibility-reason.md).

**GitHub 403 rate-limit classification.** A GitHub write-probe 403 is not always a missing-permission denial.
Classification keys on explicit rate-limit response headers: a 403 with `X-RateLimit-Remaining: 0` or a present `Retry-After` header is treated as rate-limit exhaustion and produces `Result.Fail(RateLimitExhausted)` → `Unknown(RateLimited)`.
A 403 with headroom or no rate-limit headers stays `Missing` → `Denied` → `CannotPush` (fail-closed for genuine permission denial).
See ADR 0055 and [ADR 0040](docs/adr/0040-probe-403-classification.md).

A manual "re-check" action forces immediate re-evaluation of both checks.
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
A `DetectedIssue` with blockers transitions to `BlockedIssue` instead of `FreshQueuedIssue`.
A `FreshQueuedIssue` that gains blockers is demoted to `BlockedIssue`.
When all blockers are resolved — that is, closed in the provider — a `BlockedIssue` transitions to `FreshQueuedIssue`.

## Review Issue

A lifecycle state for an issue whose worker completed successfully and produced a PR.
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, and `FeedbackCutoffAt` — all non-nullable.
Awaits human review of the PR. The monitoring service polls the provider for PR/issue status and review feedback.
`FeedbackCutoffAt` filters stale feedback — only review comments submitted after this timestamp are considered actionable. Set to the worker run's completion time on first entry; updated on re-entry after a revision cycle.
Transitions: `Revise()` → `RevisionQueuedIssue` (feedback detected); `Complete()` → `CompletedIssue` (issue closed); `Fail()` → `ContinuableFailedIssue` (PR closed without merge — branch exists).

## Unchanged Issue

A lifecycle state for an issue whose worker completed successfully (exit code 0) but produced no code changes — no branch, no PR.
Carries `WorkerRunId` — non-nullable.
Requires manual resolution: the user can retry (disagreeing with the worker's assessment).
Classified as Active / Needs attention (see [Issue Lifecycle Partition](#issue-lifecycle-partition)) — it cannot resolve itself.
Transitions: `UnchangedIssue.Retry()` → `FreshQueuedIssue`.
Hard-deleted when the provider-side issue is closed or loses its trigger label (untracked by the poller). If reopened upstream with the trigger label it is re-detected as a new issue.

## Fresh Queued Issue

The first-time dispatch tier — the lowest-priority of the three queued variants (`TierRank = 2`).
An issue reaches this state from three paths: `DetectedIssue` with no blockers, an unblocked `BlockedIssue` (all blockers resolved), or a retried `FailedIssue` / `UnchangedIssue`.
`DispatchBranchName` is generated fresh from the issue kind prefix, number, and title (`BranchName.Generate(IssueKind.BranchPrefix, IssueNumber, Title)`).
`Context` is `DispatchContext.Fresh(branchName)` — instructs the worker to start a new implementation on a new branch.
Transitions: `Claim()` → `InProgressIssue` (worker assigned); `Block()` → `BlockedIssue` (blockers detected).

## Revision Queued Issue

A lifecycle state for an issue queued for revision after receiving review feedback.
Carries `BranchName`, `PullRequestUrl`, and `ReviewComments` (`IReadOnlyList<ReviewComment>`) — all non-nullable.
Created from `ReviewIssue.Revise()` when the monitoring service detects a "changes requested" review.
Claimed with priority over regular `FreshQueuedIssue` to minimize open issue count.
Transitions: `Claim()` → `RevisionInProgressIssue`.

## Revision In-Progress Issue

A lifecycle state for an issue whose worker is executing a revision cycle.
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, and `ReviewComments` (`IReadOnlyList<ReviewComment>`) — all non-nullable.
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
Carries `WorkerRunId`, `FailureReason` (string description), `FailureCategory`, and `FailedAt`.
Can come from `InProgressIssue` (worker failed before pushing a branch) or `ReviewIssue` (PR closed without merge, no branch recovery needed).
Transitions: `FailedIssue.Retry()` → `FreshQueuedIssue` (fresh run, no branch context).

## Continuable Failed Issue

A lifecycle state for an issue whose worker run failed but left recoverable work on a pushed branch.
Carries `WorkerRunId`, `BranchName`, `FailureReason`, `FailureCategory`, and `FailedAt` — all non-nullable.
Optionally carries `PullRequestUrl` — present when created from `ReviewIssue.Fail()` (PR was closed without merge), absent when created from `InProgressIssue` (no PR existed).
Created from `InProgressIssue` when the failed run left commits on the branch — Foundry checks via `GetBranchCommitSummaryAsync` against the provider after the container exits; commit count > 0 is the branch-has-commits boolean.
Also created from `ReviewIssue.Fail()` since `ReviewIssue` always has a branch.
Retry dispatches a continuation run that checks out the existing branch and resumes implementation.
Transitions: `ContinuableFailedIssue.Retry()` → `ContinuationQueuedIssue` (resumes existing branch).

## Continuation Queued Issue

A lifecycle state for an issue queued for continuation from an existing branch with prior work.
Carries `BranchName` and `FailureReason` — both non-nullable. `FailureReason` is copied from the originating `ContinuableFailedIssue` and truncated to 500 characters by the aggregate.
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
Carries `WorkerRunId`, `BranchName`, `PullRequestUrl`, `ReviewComments` (`IReadOnlyList<ReviewComment>`), `FailureReason`, `FailureCategory`, and `FailedAt` — all non-nullable.
Created from `RevisionInProgressIssue.MarkFailed()`.
Preserves branch context so retry re-enters the revision path.
Transitions: `Retry()` → `RevisionQueuedIssue` (re-enters revision path with existing branch).

## Operator-Triggered Retry

A manual action available on any retry-supporting issue via `POST /api/issues/{id}/retry`.
Dispatches polymorphically on the loaded issue state: `FailedIssue.Retry()` → `FreshQueuedIssue` (fresh run); `ContinuableFailedIssue.Retry()` → `ContinuationQueuedIssue` (resumes existing branch); `RevisionFailedIssue.Retry()` → `RevisionQueuedIssue` (re-enters revision path); `UnchangedIssue.Retry()` → `FreshQueuedIssue` (fresh run, operator disagrees with worker assessment).
Any non-retryable state returns a conflict error with no state change.

## Transient Retry

A bounded, automatic retry for issues that failed with `FailureReason.TransientApiError`, run by `TransientRetryService` (a periodic background service in the Issues module, 60 s tick).
Each tick, it loads `FailedIssue` / `ContinuableFailedIssue` candidates whose `failure_category` is `transient_api_error` (a SQL-filterable column) with a coarse `FailedAt <= now - InitialBackoff` prefilter, then decides per candidate in memory: the attempt count is the number of leading consecutive transient runs derived from the append-only `worker_runs` rows (`IWorkerRunQueries.CountConsecutiveTransientRunsAsync`, bounded at `MaxTransientRetries + 1` materialized rows), and the issue is due when `FailedAt + backoff(attempt)` has elapsed.
Due candidates are re-queued through the existing `Retry()` transition — `FailedIssue` → `FreshQueuedIssue`, `ContinuableFailedIssue` → `ContinuationQueuedIssue` — so a de-labelled or already-retried issue is a safe no-op (a concurrent manual retry that moved the issue out of the failed state is tolerated).
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
On each poll cycle, the poller emits a `ProviderIssueUntracked` integration event for any tracked issue absent from that fetch — but only when that fetch is provably complete.
The provider clients stop paginating at a fixed page cap and return `IssueListing.IsComplete: false` when the cap is reached with pages still outstanding.
An incomplete listing cannot distinguish an issue closed on the provider from one that simply fell outside the fetched window, so the untrack pass is skipped entirely and the repository enters **untrack suppression**, logged once on entry rather than every cycle.
Detection, detail-change, dependency, and review passes all continue normally and the poll still succeeds — only deletion is withheld.
Suppression clears automatically on the first later cycle whose listing is complete; no operator action is involved.
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
`ActiveRun` also carries `BranchCommitCount` (non-nullable int, default 0) and `LastObservedCommitSha` (nullable string) — see Branch Commit Count and Worker Activity Observation Loop below.

**`StartingRun` bounded lifetime.**
`StartingRun` is the state a worker run occupies between row creation (in `IssueClaimedHandler`) and container start (`StartAsync`).
This window exists because the row must be committed before the Docker API is called — a crash or daemon failure in that gap would otherwise leave the issue in `in_progress` with no recovery path.
`StartingRun` rows are bounded to at most `StaleStartingRunThreshold` (10 minutes) by the periodic sweep (see Orphan Reconciliation).
After that threshold, the sweep fails the run via `StartingRun.Fail(ContainerError)`, which raises `WorkerRunFailed`, which the Issues module's `WorkerRunFailedHandler` turns into `InProgressIssue → FailedIssue` — making the issue retryable from the dashboard (see [ADR 0050](docs/adr/0050-recover-stranded-starting-runs-via-the-failure-bridge.md)).

## Worker Slot Occupancy

The count of dispatch slots held at any moment — the figure checked against `MaxConcurrent` before a new run is dispatched.
Slot occupancy is the union of reservations, starting runs, and active runs (`reservations ∪ starting ∪ active`): all three states count against the limit.
Including `DispatchReservation` in the count prevents over-dispatch during the window between the `WorkerCapacityAvailable` event being published and the claim being processed; including `StartingRun` prevents over-dispatch during the window between row creation and container start.
An unresolved `DispatchReservation` holds its slot for at most `StaleReservationThreshold` (2 minutes) before `StaleReservationService` deletes it.
An unresolved `StartingRun` holds its slot for at most `StaleStartingRunThreshold` (10 minutes) before `StaleStartingRunService` fails it (see Orphan Reconciliation).
The query is implemented by `DbContext.GetSlotOccupancyCountAsync` and `DbContext.GetSlotOccupancyRunIdsAsync` extension methods, used by both `WorkerDispatchService` (dispatch gate) and `StaleStartingRunService` (orphan reaping).

## Branch Commit Count

The number of commits the issue branch is ahead of the repository's default branch by merge-base comparison.
This is a projection of provider truth, not a Foundry-maintained tally — Foundry queries the provider on each observation tick and stores the result on `ActiveRun.BranchCommitCount`.

**Provider mechanics.**
GitHub: `GET /repos/{owner}/{repo}/compare/{default}...{branch}` — the `ahead_by` field in the response gives the commit count; the last entry in `commits` gives the head SHA.
GitLab: `GET /projects/{path}/repository/compare?from={default}&to={branch}` — the compare endpoint defaults to merge-base comparison (never straight); `commits.Count` is the commit count and the last entry's `id` is the head SHA.

**Change detection.**
`ActiveRun.LastObservedCommitSha` stores the head SHA seen on the previous tick.
`RecordBranchCommitCount(count, sha, observedAt)` persists both `BranchCommitCount` and `LastObservedCommitSha` and raises `WorkerActivityObserved` whenever *either* the count *or* the SHA differs from the stored values; it returns early — no write, no broadcast — only when both match.
Keying dedup on the count as well as the SHA means a genuine count change is never silently discarded, even on the first observation where both the incoming and stored SHA are null (the GitHub `commits` cap can also repeat a stale tip SHA while the count still advances).
`LastObservedCommitSha` is persisted (not in-memory), so dedup survives a host restart.

**Rebase behaviour.**
The count is not monotonically clamped — a rebase reduces it.
A `NotFound` provider response (branch deleted) resets the count to 0 and clears the SHA.
Any other provider error (transient network failure, etc.) leaves the persisted count and SHA unchanged and broadcasts nothing.

## Worker Activity Observation Loop

This loop is reached only for a run whose container the tick has already confirmed alive (`status.IsRunning`). On each ~10 s `WorkerDispatchService` tick, for every such `ActiveRun` the service:

1. Calls `ActiveRun.RecordActivity(now)` unconditionally — the container is provably running, so `LastActivityAt` advances every tick and `WorkerActivityObserved` is raised (monotonic: an out-of-order timestamp is ignored). Liveness is derived from container-alive, not from log growth — the worker buffers its stdout until exit, so log length is not a progress signal.
2. Calls `GetBranchCommitSummaryAsync` to project the current branch commit count from the provider:
   - **Success** — calls `RecordBranchCommitCount(summary.CommitCount, summary.LatestSha, now)`; a `WorkerActivityObserved` event is raised whenever the count or the head SHA changed.
   - **`NotFound`** — calls `RecordBranchCommitCount(0, null, now)`; resets a previously non-zero count to 0 and raises `WorkerActivityObserved`.
   - **Any other failure** — logs one structured warning and leaves the persisted count unchanged; `RecordBranchCommitCount` is not called for the error, so no count-change event is raised (the container-alive `RecordActivity` from step 1 still stands).
3. If any domain events were raised, saves to the database (within the same scope transaction) and dispatches the events.

`LastActivityAt` therefore means "last confirmed alive"; the branch commit count is the independent "did something" signal. A hung worker stays distinguishable from a healthy one — its `LastActivityAt` keeps ticking while its commit count stops advancing.

`WorkerActivityObserved` is handled by `WorkerActivityObservedHandler`, which broadcasts a `WorkerActivity` payload (`WorkerRunId`, `IssueId`, `LastActivityAt`, `CommitCount`) to all connected dashboard clients via `WorkerHub`.

## SignalR Worker Activity Replay

`WorkerHub.OnConnectedAsync` replays a `WorkerActivity` payload for every active run to the connecting client, so a dashboard reload or reconnect renders the current commit count (and last-activity time) without waiting for the next observation tick.
Live pushes and connect-replay use the same `WorkerActivity` client method — the frontend makes no distinction between them.

**Dashboard display.**
The in-progress issue card headline shows the branch commit count.
Log silence (the wall-clock gap since the last log output) is a frontend-only signal shown only past a 5-minute threshold — it is rendered alongside the commit count, not instead of it.

## Worker Outcome Detection

How Foundry establishes what a worker accomplished, resolved by `WorkerOutcomeResolver` — a pure, side-effect-free function applied after the container exits (and on timeout, orphan-reconcile, and container-not-found paths).

**MR-state-first lookup.**
The resolver queries `GetMergeRequestByBranchAsync`, keyed on the run's stored `BranchName` (which survives remote branch deletion).
The resulting presence maps to outcomes:

- `Merged` → `Completed` (transitions `InProgressIssue` → `CompletedIssue`)
- `Open` → `Review` (transitions `InProgressIssue` → `ReviewIssue`)
- `Closed` (unmerged) + branch has commits → `ContinuableFailure`; otherwise → `Failure`
- `None` (no MR found) → fall back to exit-code + branch-commits path (see below)

The "branch has commits" boolean is derived from `GetBranchCommitSummaryAsync` (commit count > 0) — the single consolidated query behind both the closed-MR and no-MR paths.

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
Any unresolved run that exceeds the ceiling is processed through the same resolver → applier pair (exit code null; MR-state still consulted first), so no active run can hold a worker slot indefinitely.
The ceiling derives from `StartedAt`, which only `ActiveRun` carries — the watchdog therefore covers active runs only, and a run that never reaches `ActiveRun` is outside it.
Runs stranded in `StartingRun` (never reaching `ActiveRun`) are handled by the periodic sweep instead — see Orphan Reconciliation.

**Docker daemon reachability.**
`GetStatusAsync` returns a discriminated `WorkerStatusProbe` distinguishing three cases:

- `Available` — container exists (running or exited); carries a `WorkerStatus` with `IsRunning`, `ExitCode`, and `FinishedAt`.
- `NotFound` — container is definitively gone (Docker 404).
- `Unreachable` — daemon connectivity failure: only `HttpRequestException`, `TimeoutException`, or a self-timeout `OperationCanceledException` (caller's token was NOT already cancelled).
  Any other `DockerApiException` propagates as an unexpected error.

On `Unreachable` the active run is left in `ActiveRun` state (never failed) and recovers on a later tick.
Startup reconciliation is deferred when the daemon is unreachable — the `_reconciled` latch is set only after every per-run `GetStatusAsync` probe returns without an `Unreachable` result; a boot with zero `ActiveRun` rows latches `_reconciled` immediately without contacting Docker.
Orphan-container reaping is not part of the startup reconcile: it has moved to the always-on periodic sweep (`StaleStartingRunService`), which defers its own tick when `ListByLabelAsync` returns `Unreachable` — see Orphan Reconciliation.

A `/ready` readiness health check (tagged `ready`, mapped unconditionally at `/ready`) surfaces Docker daemon connectivity:
healthy when the daemon responds to a ping within 3 seconds, unhealthy with a message identifying Docker daemon connectivity when the ping fails or times out.

## Orphan Reconciliation

The periodic sweep that detects and cleans up containers and runs that have fallen out of the normal lifecycle — implemented by `StaleStartingRunService` (a `PeriodicBackgroundService` in the Workers module, ticking every minute).

**Orphaned container reaping.**
Each tick, the sweep calls `IWorkerOrchestrator.ListByLabelAsync` to enumerate every container carrying the `foundry.managed` label (worker-managed containers), then cross-references the returned run IDs against slot occupancy (see Worker Slot Occupancy).
Any labelled container whose run ID is outside the `starting ∪ active` set is an orphan — its run ended (or was lost) but the container was not cleaned up.
The sweep stops and removes each orphan container immediately.

**Stale `StartingRun` failure.**
After reaping orphaned containers, the sweep loads all `StartingRun` rows and applies the staleness threshold (`StaleStartingRunThreshold` = 10 minutes).
A `StartingRun` older than the threshold is stale: the container either never started or the host died before activation.
For each stale run, the sweep:

1. Reaps the run's container first, if a labelled container with that run ID was found by `ListByLabelAsync`.
2. Calls `StartingRun.Fail(ContainerError("Container did not start within the allowed time."))`.
3. The `WorkerRunFailed` domain event raised by `Fail` flows through `WorkerRunFailedHandler` → `InProgressIssue.MarkFailed()` → `FailedIssue` → the issue becomes retryable from the dashboard (see ADR 0050).

A `container_error` failure does not auto-retry (`TransientRetryService` filters on `transient_api_error` only) — the issue parks under "Needs attention" for operator review, consistent with [ADR 0014](docs/adr/0014-remove-immediate-requeue-always-pause.md).
A `WorkerRunFailed` event whose run ID does not match the issue's current `WorkerRunId` is silently ignored by `WorkerRunFailedHandler` (stale-run-ID guard), so a concurrent transition cannot produce a double failure.
The same guard applies to `WorkerRunCompletedHandler`: a `WorkerRunCompleted` event whose run ID does not match is rejected as stale, so a superseded run cannot drive a completed transition on an issue that has already moved on.

**Daemon-unreachable defer.**
When `ListByLabelAsync` fails with a daemon connectivity error (detected by `DockerDaemonConnectivity.IsUnreachable`), the sweep logs a warning and returns without reaping or failing anything — both phases are skipped for that tick.
Per-run failure attempts that encounter Docker errors are also best-effort: a `TryStopAndRemoveAsync` failure is logged but does not abort the failure transition for the run.

## Container Output

The captured tail of a failed worker container's stdout/stderr, stored on `FailedRun` as a nullable string.
Captured by Foundry (not the worker) from the Docker API after the container stops but before removal.
Best-effort — null when the container is already gone, never started, or the Docker API call fails.
Docker timestamps are enabled (`--timestamps` flag) so each output line carries an RFC 3339 prefix, providing a unified timeline.

Failed-run output is served by `GET /api/workers/runs/{workerRunId}/log` (200 with text body when available, 204 when absent, 404 when the run is unknown) and rendered in the dashboard by the shared `fd-log-view` component (static source mode) in the issue detail panel.

Running workers stream live output via the `WorkerHub.StreamLog(workerRunId)` method on `/hubs/workers` — the hub method returns an `IAsyncEnumerable<string>` of redacted log lines with backlog replay followed by live follow.
The dashboard's `fd-log-view` component subscribes to this stream (stream source mode) and renders the redacted lines in arrival order.

## First-Run Wizard

A guided setup flow (`/setup`) that runs when no accounts are configured.
Three steps: select auth mode (API key or OAuth), add first account (provider, base URL, PAT — the account name is derived from the token identity), select repositories to monitor (fetched from provider API).
Auto-redirects from `/issues` when no accounts exist. Redirects to `/issues` on completion.
The wizard reuses the same form components as the settings page.

## FailureReason

A value object on FailedRun that classifies how the run failed.
Variants: `NonZeroExit(exitCode)` (container exited with non-zero code), `TimedOut` (exceeded configured timeout), `ContainerError(message)` (Docker-level failure — image not found, daemon unavailable, etc.), `UsageLimited(resetsAt)` (worker hit a *time-based* Anthropic API usage limit — session, weekly, or Opus quota — carrying a parseable reset time; token `usage_limited`), `CreditsExhausted` (worker hit a *money-based* 429 — credit pool empty, org monthly spend limit reached, or the CLI ≥ 2.1.119 regression — carrying no reset time; token `credits_exhausted`; summary `"Credits exhausted"`, which deliberately shares no prefix with the usage-limit summary so the auto-resume `LIKE 'Usage limit reached%'` sweep cannot catch it; blocks the account's spend indefinitely until an operator resumes), `WorkerBootstrapFailed(detail)` (pre-task failure — the worker container died during entrypoint bootstrap, before `claude` ran; carries a short, secret-redacted diagnostic `Detail` with the failed stage and error tail), `AuthInvalid` (the worker's Claude credentials were rejected — `api_error_status == 401` or `error.type == "authentication_error"`; triggers the auth-invalid pause), `TransientApiError` (a transient Anthropic API fault the run neither caused nor can fix — `api_error_status` in the 5xx range, or `is_error: true` with `api_error_status: null` and a `result` matching a known transient phrase such as a mid-response connection drop or `529 Overloaded`; token `transient_api_error`; drives the bounded auto-retry described under Transient Retry), `ProviderError(message)` (a provider API call needed to start the run failed, e.g. branch pre-creation rejected with 403; raised before the worker task begins).
Each variant's `CategoryToken` value flows from the matching `FailureCategory` const (see FailureCategory below), making `FailureCategory` the single source of truth for the token vocabulary.

## FailureCategory

A value object in `Foundry.Modules.Workers.Contracts` owning the ten-token vocabulary that names *why* a run failed, and the single owning type for that vocabulary (see [ADR 0068](docs/adr/0068-failurecategory-value-object-owns-token-vocabulary.md)).
The ten tokens are the nine `FailureReason` category tokens plus `pr_closed`.
`FailureReason`'s nine `CategoryToken` values derive from the matching `FailureCategory` const fields, so both producers obtain their value from the owning type: `WorkerRunFailedHandler` converts `FailureReason.CategoryToken` at the domain boundary, and `ProviderPullRequestClosedHandler` uses `FailureCategory.PrClosed` directly.
The entity property on `FailedIssue`, `ContinuableFailedIssue`, and `RevisionFailedIssue` is typed `FailureCategory` and persisted via an EF `ValueConverter` to the unchanged `TEXT` column (`failure_category`) — no migration needed, and the `TransientRetryService` predicate stays SQL-translatable by comparing against `FailureCategory.TransientApiError`.
The converter's read direction is lenient: unknown stored tokens coalesce defensively to `FailureCategory.NonZeroExit` without throwing, so a stale or legacy row never disables auto-retry. The domain-boundary conversion in `WorkerRunFailedHandler` remains the point that rejects and logs unknown tokens from incoming events.
The `WorkerRunFailed` contract `Category` field remains `string?`; rejection of unknown or null values happens at the domain boundary in `WorkerRunFailedHandler`, which falls back to `FailureCategory.NonZeroExit` (logging a warning) so the issue always transitions to failed.
Drives category-conditional recovery: `transient_api_error` is the discriminator for the bounded auto-retry described under Transient Retry.

## Usage Limit

A *time-based* state where the Anthropic API quota (session, weekly, or Opus limit) is exhausted and self-heals at a known reset time.
Detected by parsing the worker container's JSON output (`--output-format json`): the primary signal is `ResultMessage.api_error_status == 429`; the `terminal_reason` allowlist (`"blocking_limit"`, `"rapid_refill_breaker"`) is retained as a secondary signal for older output shapes. Note that a 429 can arrive with `subtype: "success"` and `terminal_reason: "completed"`, so neither field is reliable on its own.
The reset time is extracted from the human-readable result text. Two wall-clock wording shapes are handled: `resets <time> (UTC)` (e.g. `"You've hit your limit · resets 12:10am (UTC)"`) and `reset at <time> (<IANA zone>)` (e.g. `"Your limit will reset at 3pm (America/New_York)."`). In both forms the time is 12-hour; a bare hour without minutes (e.g. `3pm`) defaults minutes to `00`. UTC times resolve to the next future UTC occurrence directly; IANA zone names are converted to UTC via the system timezone database, then the same roll-forward applies. ISO-8601 timestamps in the result text take precedence over wall-clock parsing.
Whether a reset time parses is the discriminator between the two 429 classes (see [ADR 0046](docs/adr/0046-parseable-reset-time-as-usage-limit-discriminator.md)): a 429 with a parseable reset time is a `UsageLimited` time-based limit; a 429 with no parseable reset time is a money-based block classified as `CreditsExhausted` (see FailureReason and Dispatch Pause). There is no fabricated-cooldown fallback — the former `DefaultCooldownMinutes` setting is removed.
A detected usage limit always triggers a global dispatch pause via `GlobalSettings.UsageLimitResetsAt` — there is no immediate-requeue path. `GlobalSettings.SetUsageLimitResetsAt` remains extend-only and clamps to 7 days, so a reset time already in the past only ever extends an existing pause.
The triggering issue transitions to `FailedIssue` / `ContinuableFailedIssue` with `FailureReason.UsageLimited(resetsAt)`.
On detection, `WorkerDispatchService` raises the `DispatchPaused` integration event, which is broadcast as a `dispatch` system notification (`isActive: true`) so the dashboard usage-limit banner updates live without a page refresh.

## Dispatch Pause

A global operational state where the dispatch loop skips issuing new work.
Two independent global triggers on `GlobalSettings`: `UsageLimitResetsAt` (automatic, from usage limit detection) and `IsDispatchPaused` (manual, from operator "Pause All" action).
Dispatch is paused when either is active.
Auto-resume clears `UsageLimitResetsAt` and retries all `FailureReason.UsageLimited` issues when `AutoResumeOnUsageReset` is enabled.
Manual "Resume All" clears both flags and retries usage-limited issues.

A third, independent pause is the **credit block**: when a run fails with `CreditsExhausted`, the `ClaudeAccount` aggregate's `SpendState` transitions to `Blocked(NextProbeAt)`, and `CredentialGate.CanDispatchAsync` then refuses dispatch regardless of the global flags. This pause carries no reset time — a money-based block cannot say when it clears — but it clears without an operator once the account can spend again (Team credits auto-reload, an admin raises the spend cap), so the block schedules a **Credit Probe** to poll for that moment (see Credit Probe). `SpendState` and `CredentialValidity` are independent: an account can be simultaneously credit-blocked and auth-invalid, the gate refuses on either, and clearing one leaves the other in force. Both a successful probe and a manual "Resume All" call `RestoreSpend()` and — only when the state actually changed — publish `CreditsRestored`, on which the Issues module re-queues every issue whose `FailureReason` equals `"Credits exhausted"` (an exact match, never a prefix sweep); the transition is idempotent so `CreditsRestored` publishes exactly once even when a probe and a manual resume converge.
Already-running workers are unaffected — only queued issues are held.
Both pause and resume broadcast a `dispatch` system notification (`isActive: true` on pause, `isActive: false` on resume); the client treats this as a pure reload trigger and re-syncs banner state from `/api/settings` (the authoritative source) rather than reading any timestamp off the SignalR payload.

## Credit Probe

The mechanism that auto-clears a credit block. Since the clearing moment is unknowable, Foundry polls for it with a cheap transient container rather than re-dispatching real work — a full resume would re-queue every stranded issue each cycle (N worker containers, N×2+ provider API calls, N `worker_runs` rows, N failed transitions, N broadcasts), while a probe reads the same single bit ("can this account spend right now?") with one short container and no issue state touched.

`SpendState.Blocked` carries a durable `NextProbeAt` — kept on the aggregate (not an in-memory timer) so the schedule survives a host restart. `CreditProbeService` (a `PeriodicBackgroundService` in the Credentials module, mirroring `TransientRetryService`) ticks ~30 s, and when `NextProbeAt` has passed it delegates to `CreditProbeCoordinator.TryRunProbeAsync`. The coordinator holds an in-process `SemaphoreSlim(1,1)` for single-flight — a second concurrent caller (a scheduled tick racing the operator's "Check now") is a no-op that reports the in-flight probe. It defers while `ILoginSessionState.IsLoginActive` (the same window `CredentialGate` refuses on) and logs-and-skips when no `ClaudeAccount` row exists.

The probe container mirrors the login/credential-helper containers: transient (`foundry.transient=true` so existing reaping applies and worker reaping never touches it), `foundry.role=credit-probe`, `AutoRemove`, built from the login image, running a trivial `claude -p` prompt under its own `timeout`, mounting the credential volume read-only (OAuth) or injecting `ANTHROPIC_API_KEY` (API key). Its captured logs are classified by `IProbeOutcomeClassifier` (a public surface in `Workers.Contracts` wrapping the internal `IContainerOutputParser`, so the money-vs-time-vs-infra discrimination has a single source of truth) into a `ProbeOutcome`:

- `Available` → `RestoreSpend()`; if changed, publish `CreditsRestored` (outbox) → Issues re-queues.
- `CreditsStillBlocked` (money-based 429, no reset time) → `RearmProbe(now + ProbeIntervalMinutes)`; no issue state changes.
- `UsageLimited(resetsAt)` (time-based 429, reset time parses) → set `GlobalSettings.UsageLimitResetsAt` and clear the credit block (the money path is proven fine, so the usage-limit pause takes over) rather than blindly re-arming the probe.
- `InfrastructureFailure` (container start error, timeout, or unclassifiable output) → `RearmProbe(now + ProbeIntervalMinutes)` with no `SpendState`/issue change; the banner deliberately does not report a credit problem for an infrastructure fault.

The operator can force an immediate probe regardless of `NextProbeAt` via `POST /api/credentials/probe` ("Check now"), which returns an in-flight indicator (`202`) when a probe is already running. `NextProbeAt` is exposed on the `/api/credentials` payload so the dashboard `credits` banner renders a live countdown. Container hardening for this new container type is deferred to #60.

## System Notification

A lightweight SignalR broadcast (`Category`, `IsActive`, `Message`) delivered to all dashboard clients via the system hub, used for global operational banners.
Categories in use: `dispatch`, `docker`, `image-build`, `auth`, `license`, `credits`.
Client semantics: an `isActive: true` notification is stored, replacing any prior entry for its category; `isActive: false` removes the category's entry.
Both `dispatch` and `image-build` are pure reload triggers — each broadcast carries an empty message and signals the client to re-sync authoritative state from `/api/settings` rather than reading any payload (see Dispatch Pause and Worker Image Build State).
The `credits` category (see Credit Probe) is its own reload trigger — `isActive: true` on credit block or a probe re-arm, `isActive: false` on restore — re-syncing the countdown from `/api/credentials`. It is deliberately independent of `auth`: an auth-invalid condition and a credit block are independent and can hold at once, so each holds its own category and both banners render simultaneously rather than one replacing the other.
Ephemeral — broadcast directly, never through the transactional outbox, since there is no durable consumer.

## Container Output Parser

An infrastructure service (`IContainerOutputParser`) that classifies a worker container's JSON output.
Takes raw JSON from `--output-format json` and returns a discriminated result: `NormalExit`, `UsageLimited(DateTimeOffset ResetsAt)`, `CreditsExhausted`, `AuthInvalid`, `TransientApiError`, `ParseFailure(string RawOutput)`, `NoResultLine`, or `WorkerBootstrapFailed(string Detail)`.
Inspects `ResultMessage.api_error_status` (429) as the primary limit signal, with the `terminal_reason` allowlist as a secondary signal. When a limit signal is present, reset-time parse **success** is the discriminator (see ADR 0046): if a reset time is extracted from the result text via best-effort regex (wall-clock or ISO-8601) the result is `UsageLimited(resetsAt)`; if none parses the result is `CreditsExhausted`. `Parse` takes no cooldown fallback parameter — the former fabricated `DefaultCooldownMinutes` reset time is removed.
Auth failures are detected from `api_error_status == 401`, with `error.type == "authentication_error"` as a secondary guard.
Transient API faults are detected from `api_error_status` in the 5xx range (the durable signal), or — when `api_error_status` is null and `is_error: true` — from a narrow frozen allow-list of transient `result` phrases (`"API Error: Connection closed mid-response"`, `"API Error: 529 Overloaded"`) matched case-sensitively as a substring. The predicate is status-5xx-or-allow-list, never a bare `is_error: true`, so genuine task failures are never laundered into a retryable category. When `is_error: true` matches no known category (usage-limit, auth, transient), the parser logs a warning naming the unclassified `result` text — a tripwire for a reworded transient phrase that the brittle allow-list would otherwise miss silently.
Bootstrap failures are detected via a sentinel line (`FOUNDRY_BOOTSTRAP_FAILED stage=… detail=…`) emitted by `entrypoint.sh` when the container dies before `claude` runs (clone, auth, or branch stage); the parser scans for the sentinel only when no Claude JSON result line is present, so a genuine result always wins; a non-zero exit with no result line and no sentinel falls back to `WorkerBootstrapFailed` heuristically.
Domain types remain JSON-unaware — all parsing is in infrastructure.
`IContainerOutputParser` and `ContainerOutputParseResult` are `internal` to the Workers module; the Credentials credit probe (which runs outside Workers) reaches the same classification through `IProbeOutcomeClassifier`/`ProbeOutcome` in `Workers.Contracts`, a public wrapper that maps each parse result to the coarser probe outcome (`Available`, `CreditsStillBlocked`, `UsageLimited`, `InfrastructureFailure`) so there is one source of truth for the money-vs-time-vs-infrastructure discrimination.
