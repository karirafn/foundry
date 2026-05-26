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

The background process that polls configured repositories for issues with trigger labels.
Runs on a configurable interval per repo with a global default.

## Issue

A provider-side issue tagged for Foundry processing.
Modeled as a polymorphic aggregate — each lifecycle state is a distinct type (`DetectedIssue`, `QueuedIssue`, `InProgressIssue`, `ReviewIssue`, `CompletedIssue`, `FailedIssue`).
State transitions are methods on each variant that return the next variant type, enforcing valid transitions at compile time.

## Monitored Repository

A repository configured for Foundry to poll.
References an Account (for credentials) and specifies a trigger label and optional per-repo poll interval.
Uniquely identified by its Repository Slug within the context of its Account.

## Issue Dependency

A directed relationship between two issues where one blocks the other.
Both GitHub and GitLab expose dependencies via structured APIs (not body text).
Foundry persists these relationships during detection so the Workers module can respect execution order.

## Lifecycle Label

A provider label applied to an issue to reflect its current state in the Foundry pipeline.
Each state transition replaces the previous lifecycle label (e.g., remove `foundry-ready`, add `foundry-detected`).
The trigger label (e.g., `foundry-ready`) is the initial lifecycle label that causes Foundry to detect the issue.

## Repository Slug

The `owner/name` pair that uniquely identifies a repository within a provider.
Value object — always appears as a pair, never just owner or just name.

## Run

A single execution of a worker against an issue.
An issue can have multiple runs (e.g. after retry from Failed or Review state).
Each run captures logs, exit status, and PR/MR URL.
