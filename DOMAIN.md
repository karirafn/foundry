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
Workers clone the repo, implement the issue, push a branch, and write reports to a shared volume.

## Provider

A git hosting platform (GitHub, GitLab).
Each provider has its own API client, label format (flat on GitHub, scoped on GitLab), and CLI tooling (`gh`, `glab`).

## Account

Credentials for accessing a specific provider's API.
Modeled as polymorphic variants (`GitHubAccount`, `GitLabAccount`) — each provider may carry provider-specific configuration (e.g., API base URL for self-hosted instances).
The actual PAT is stored externally (user secrets / Key Vault); the Account holds a `SecretKeyName` referencing the configuration key.
Multiple accounts can exist per provider. A Monitored Repository references a specific Account.

## Monitor

The background process that polls configured repositories for issues labeled `foundry`.
Runs on a fixed tick interval (30s default) and checks each repo's eligibility based on its configured poll interval and `LastPolledAt` timestamp.

## Issue

A provider-side issue tagged for Foundry processing.
Modeled as a polymorphic aggregate — each lifecycle state is a distinct type (`DetectedIssue`, `BlockedIssue`, `QueuedIssue`, `InProgressIssue`, `ReviewIssue`, `UnchangedIssue`, `CompletedIssue`, `FailedIssue`).
State transitions are methods on each variant that return the next variant type, enforcing valid transitions at compile time.

## Monitored Repository

A repository configured for Foundry to poll.
References an Account (for credentials) and specifies an optional per-repo poll interval.
Uniquely identified by its Repository Slug within the context of its Account.
Tracks `LastPolledAt` for per-repo poll timing.

## Issue Dependency

A directed "blocked by" relationship between two issues within the same repository.
Stored as a collection of blocking issue numbers on the Issue aggregate — the blocking issue may or may not be tracked by Foundry.
Both GitHub (REST API v2026-03-10) and GitLab (Issue Links API, Premium+) expose dependencies via structured APIs.
Foundry fetches dependencies during the detection poll cycle and reconciles them on each subsequent poll.
Circular dependencies are detected by a domain service and flagged via a `CircularDependencyDetected` domain event.

## Blocked Issue

A lifecycle state for an issue that has unresolved dependencies.
A `DetectedIssue` with blockers transitions to `BlockedIssue` instead of `QueuedIssue`.
A `QueuedIssue` that gains blockers is demoted to `BlockedIssue`.
When all blockers are resolved, a `BlockedIssue` transitions to `QueuedIssue`.

## Review Issue

A lifecycle state for an issue whose worker completed successfully and produced a PR.
Carries `WorkerRunId`, `BranchName`, and `PullRequestUrl` — all non-nullable.
Awaits human review of the PR. The monitoring service polls the provider for PR/issue status.
Transitions: `ReviewIssue.Retry()` → `QueuedIssue`; monitoring-driven → `CompletedIssue` (issue closed) or `FailedIssue` (PR closed without merge).

## Unchanged Issue

A lifecycle state for an issue whose worker completed successfully (exit code 0) but produced no code changes — no branch, no PR.
Requires manual resolution: the user can complete the issue (agreeing no changes are needed) or retry (disagreeing with the worker's assessment).
Transitions: `UnchangedIssue.Complete()` → `CompletedIssue`, `UnchangedIssue.Retry()` → `QueuedIssue`.

## Completed Issue

Terminal lifecycle state — the issue is resolved.
Carries nullable `BranchName` and `PullRequestUrl` (present when completed via review, absent when completed from `UnchangedIssue`) and `CompletedAt`.

## Failed Issue

A lifecycle state for an issue whose worker run failed or whose PR was closed without merge.
Carries `WorkerRunId`, `FailureReason` (string description), and `FailedAt`.
Can come from `InProgressIssue` (worker failed) or `ReviewIssue` (PR rejected — concern 2).
Transitions: `FailedIssue.Retry()` → `QueuedIssue`.

## Trigger Label

The constant label `foundry` applied to a provider-side issue to flag it for Foundry processing.
The label stays on the issue untouched — Foundry does not add, remove, or swap labels on the provider.
All lifecycle state is tracked internally in the database.

## Provider Issue

A DTO representing an issue as returned by a provider's API.
Carries raw data (number, title, body, author username, URL, labels) that the domain maps into value objects and aggregates during detection.

## Repository Slug

The `owner/name` pair that uniquely identifies a repository within a provider.
Value object — always appears as a pair, never just owner or just name.

## WorkerRun

A single execution of a worker against an issue — the primary aggregate for the Workers module.
Modeled as a polymorphic aggregate with state variants: `StartingRun` (container creation requested), `ActiveRun` (container running), `CompletedRun` (exited successfully), `FailedRun` (exited with error, timed out, or failed to start).
An issue can have multiple runs (e.g. after retry from Failed or Review state).
Each run captures reports, exit status, branch name, and PR/MR URL.

## WorkerReport

A progress or final report written by a worker during execution.
Owned by a WorkerRun as a collection — persisted to the database after ingestion from the shared reports volume.
Periodic reports capture intermediate progress (e.g., "implementing step 3/6"); the final report captures the outcome (branch name, PR URL, summary, error details).
JSON format: `{ type, status, summary, error, prUrl, branchName, metrics }`.

## FailureReason

A value object on FailedRun that classifies how the run failed.
Variants: `NonZeroExit(exitCode)` (container exited with non-zero code), `TimedOut` (exceeded configured timeout), `ContainerError(message)` (Docker-level failure — image not found, daemon unavailable, etc.).
