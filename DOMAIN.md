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
Two methods: API key (`ANTHROPIC_API_KEY`, pay-per-use, stored encrypted in DB) and OAuth token (`CLAUDE_CODE_OAUTH_TOKEN`, Max/Pro/Team/Enterprise plan, auto-detected from Claude Code's `~/.claude/.credentials.json`).
Exactly one method is configured per Foundry instance — selected via auth mode in Global Settings.
OAuth mode auto-scans known credential paths across platforms (Linux, macOS, Windows), validates the token, and auto-refreshes using the stored refresh token.
Distinct from provider authentication (Account / PAT), which authenticates git operations against GitHub or GitLab.

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

Credentials for accessing a specific provider's API.
Modeled as polymorphic variants (`GitHubAccount`, `GitLabAccount`) — each provider may carry provider-specific configuration (e.g., API base URL for self-hosted instances).
The PAT is stored encrypted in the database using Data Protection API + EF Core Value Converters.
Multiple accounts can exist per provider. A Monitored Repository references a specific Account.

## Global Settings

A strongly-typed single-row entity storing all UI-configurable settings.
Includes worker settings (max concurrent, timeout, prompt templates), authentication mode (API key or OAuth), and usage limit controls (`AutoResumeOnUsageReset`, `DefaultCooldownMinutes`, `UsageLimitResetsAt`, `IsDispatchPaused`).
DB is the single source of truth — `IConfiguration` is not consulted for settings the UI manages.
Infrastructure-only settings (Docker image, mounts, memory/CPU/PID limits) remain in `IConfiguration`.

## Monitor

The background process that polls configured repositories for issues labeled `foundry`.
Runs on a fixed tick interval (30s default) and checks whether each repo is due for polling based on its configured poll interval and `LastPolledAt` timestamp.

## Issue

A provider-side issue tagged for Foundry processing.
Modeled as a polymorphic aggregate — each lifecycle state is a distinct type (`DetectedIssue`, `BlockedIssue`, `QueuedIssue`, `ContinuationQueuedIssue`, `RevisionQueuedIssue`, `InProgressIssue`, `RevisionInProgressIssue`, `ReviewIssue`, `UnchangedIssue`, `CompletedIssue`, `FailedIssue`, `ContinuableFailedIssue`, `RevisionFailedIssue`).
State transitions are methods on each variant that return the next variant type, enforcing valid transitions at compile time.

## Issue Kind

A value object on the base `Issue` type classifying the nature of the work — `Feature`, `Bug`, `Refactor`, `Documentation`, etc.
Extracted during issue detection by a provider-agnostic classifier: each label is normalized by stripping any `scope::` prefix, then the suffix is matched against the kind names — so flat (`feature`) and scoped (`type::feature`) labels both classify on either provider.
Falls back to `Feature` when no recognized label is present.
Used by `BranchName.Generate()` to derive the branch prefix (`feat/`, `fix/`, `refactor/`, `docs/`).

## Monitored Repository

A repository configured for Foundry to poll.
References an Account (for credentials) and specifies an optional per-repo poll interval.
Uniquely identified by the pair (Host, Repository Slug) — the same repo on the same host cannot be monitored through multiple accounts (prevents duplicate issue detection), while the same path on different hosts (e.g. github.com vs gitlab.com, or self-hosted instances) refers to distinct repositories. The Host is denormalized from the account's base URL at creation.
Tracks `LastPolledAt` for per-repo poll timing.
Carries a Repository Eligibility status, re-evaluated on each poll cycle.

## Repository Priority

The global dispatch order over monitored repositories.
Each Monitored Repository holds a 0-based, contiguous, unique `Position` integer — lower value means higher priority.
When a worker slot opens and multiple eligible repositories have queued issues in the same claim tier, the lowest-Position repository's issue is claimed first; ties within the same position are broken by oldest `DetectedAt`.
Tier precedence (revision → continuation → fresh) is unchanged — Repository Priority orders only within a tier.
Affects dispatch only; polling is independent of position.
New repositories append at the end (highest existing Position + 1); deleting a repository renumbers survivors contiguously.

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
Transitions: `Revise()` → `RevisionQueuedIssue` (feedback detected); `Complete()` → `CompletedIssue` (issue closed); `Fail()` → `ContinuableFailedIssue` (PR closed without merge — branch exists); `Retry()` → `ContinuationQueuedIssue` (manual restart with branch context).

## Unchanged Issue

A lifecycle state for an issue whose worker completed successfully (exit code 0) but produced no code changes — no branch, no PR.
Requires manual resolution: the user can retry (disagreeing with the worker's assessment).
Transitions: `UnchangedIssue.Retry()` → `QueuedIssue`.

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

A manual action available on any failed issue via `POST /api/issues/{id}/retry`.
Dispatches polymorphically on the loaded issue state: `FailedIssue.Retry()` → `QueuedIssue` (fresh run); `ContinuableFailedIssue.Retry()` → `ContinuationQueuedIssue` (resumes existing branch); `RevisionFailedIssue.Retry()` → `RevisionQueuedIssue` (re-enters revision path).
Any non-retryable state returns a validation/conflict error with no state change.

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
The `ProviderIssueUntrackedHandler` hard-deletes tracked records in resting states: `detected`, `queued`, `blocked`, `failed`, `continuable_failed`, `revision_failed`, `revision_queued`, and `continuation_queued`.
`completed` and `unchanged` are preserved — completion wins over provider closure.
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
Foundry derives each run's outcome after the container exits: exit status from the container, branch commits and PR/MR URL from provider queries.
`ActiveRun` carries the `BranchName` — set at activation from the dispatch payload's pre-created branch name, propagated to `FailedRun` and `CompletedRun`.

## Worker Outcome Detection

How Foundry establishes what a worker accomplished, performed entirely on the Foundry side after the container exits.
The worker runs `claude -p <WORKER_PROMPT> --append-system-prompt <SYSTEM_PROMPT> --output-format json`, pushes its branch, and opens a PR.
After exit, Foundry reads the container exit code and queries the provider: `HasBranchCommitsAsync` decides whether the branch holds recoverable work (driving `FailedIssue` vs `ContinuableFailedIssue`), and `GetPullRequestByBranchAsync` resolves the PR URL.
Foundry reads the final `claude` JSON line from stdout solely for usage-limit signals (`api_error_status` / `terminal_reason`), which produce the `UsageLimited` failure reason.
Watchdog paths (timeout, orphaned container, container not found) apply the same `HasBranchCommitsAsync` check as the clean-exit path — routing to `ContinuableFailedIssue` only when the branch has commits, and to `FailedIssue` otherwise.
This prevents a continuation retry from attempting to check out a branch that was never pushed.

## Container Output

The captured tail of a failed worker container's stdout/stderr, stored on `FailedRun` as a nullable string.
Captured by Foundry (not the worker) from the Docker API after the container stops but before removal.
Best-effort — null when the container is already gone, never started, or the Docker API call fails.
Displayed in the worker-log-panel as a collapsible section.

## First-Run Wizard

A guided setup flow (`/setup`) that runs when no accounts are configured.
Three steps: select auth mode (API key or OAuth), add first account (provider, name, base URL, PAT), select repositories to monitor (fetched from provider API).
Auto-redirects from `/issues` when no accounts exist. Redirects to `/issues` on completion.
The wizard reuses the same form components as the settings page.

## FailureReason

A value object on FailedRun that classifies how the run failed.
Variants: `NonZeroExit(exitCode)` (container exited with non-zero code), `TimedOut` (exceeded configured timeout), `ContainerError(message)` (Docker-level failure — image not found, daemon unavailable, etc.), `UsageLimited(resetsAt)` (worker hit an Anthropic API usage limit — session, weekly, or Opus quota), `WorkerBootstrapFailed(detail)` (pre-task failure — the worker container died during entrypoint bootstrap, before `claude` ran; carries a short, secret-redacted diagnostic `Detail` with the failed stage and error tail).

## Usage Limit

A state where the Anthropic API quota (session, weekly, or Opus limit) is exhausted.
Detected by parsing the worker container's JSON output (`--output-format json`): the primary signal is `ResultMessage.api_error_status == 429`; the `terminal_reason` allowlist (`"blocking_limit"`, `"rapid_refill_breaker"`) is retained as a secondary signal for older output shapes. Note that a 429 can arrive with `subtype: "success"` and `terminal_reason: "completed"`, so neither field is reliable on its own.
The reset time is extracted from the human-readable result text (e.g. `"You've hit your limit · resets 12:10am (UTC)"`): a 12-hour wall-clock time resolves to its next future UTC occurrence, ISO-8601 timestamps are also accepted, and when neither parses a configurable `DefaultCooldownMinutes` fallback is used. The fallback only ever extends an existing pause, never shortens it.
A detected usage limit always triggers a global dispatch pause via `GlobalSettings.UsageLimitResetsAt` — there is no immediate-requeue path.
The triggering issue transitions to `FailedIssue` / `ContinuableFailedIssue` with `FailureReason.UsageLimited(resetsAt)`.

## Dispatch Pause

A global operational state where the dispatch loop skips issuing new work.
Two independent triggers: `UsageLimitResetsAt` (automatic, from usage limit detection) and `IsDispatchPaused` (manual, from operator "Pause All" action).
Dispatch is paused when either is active.
Auto-resume clears `UsageLimitResetsAt` and retries all `FailureReason.UsageLimited` issues when `AutoResumeOnUsageReset` is enabled.
Manual "Resume All" clears both flags and retries usage-limited issues.
Already-running workers are unaffected — only queued issues are held.

## Container Output Parser

An infrastructure service (`IContainerOutputParser`) that classifies a worker container's JSON output.
Takes raw JSON from `--output-format json` and returns a discriminated result: `NormalExit`, `UsageLimited(DateTimeOffset ResetsAt)`, `ParseFailure(string RawOutput)`, or `WorkerBootstrapFailed(string Detail)`.
Inspects `ResultMessage.api_error_status` (429) as the primary limit signal, with the `terminal_reason` allowlist as a secondary signal, and extracts the reset time from the result text via best-effort regex (wall-clock or ISO-8601).
Bootstrap failures are detected via a sentinel line (`FOUNDRY_BOOTSTRAP_FAILED stage=… detail=…`) emitted by `entrypoint.sh` when the container dies before `claude` runs (clone, auth, or branch stage); the parser scans for the sentinel only when no Claude JSON result line is present, so a genuine result always wins; a non-zero exit with no result line and no sentinel falls back to `WorkerBootstrapFailed` heuristically.
Domain types remain JSON-unaware — all parsing is in infrastructure.
