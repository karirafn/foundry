# DOMAIN.md

Domain concepts for Foundry — a containerized service that monitors repositories for tagged issues and dispatches sandboxed AI workers to implement them.

---

## Foundry

The service itself.
Monitors repositories across multiple providers for issues with a trigger label, then dispatches sandboxed Claude Code Docker containers to implement them.

## Worker

A Claude Code Docker container dispatched to implement a single issue.
Ephemeral — created on demand, destroyed after completion.
Workers push to `foundry/<issue-id>/*` branches and call back to Foundry with results.

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
Modeled as a polymorphic aggregate — each lifecycle state is a distinct type (`DetectedIssue`, `BlockedIssue`, `QueuedIssue`, `InProgressIssue`, `ReviewIssue`, `CompletedIssue`, `FailedIssue`).
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

## Run

A single execution of a worker against an issue.
An issue can have multiple runs (e.g. after retry from Failed or Review state).
Each run captures logs, exit status, and PR/MR URL.
