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

A set of credentials (PAT) for a specific provider.
Multiple accounts can exist per provider.
A repo configuration references a specific account.

## Monitor

The background process that polls configured repositories for issues with trigger labels.
Runs on a configurable interval per repo with a global default.

## Run

A single execution of a worker against an issue.
An issue can have multiple runs (e.g. after retry from Failed or Review state).
Each run captures logs, exit status, and PR/MR URL.
