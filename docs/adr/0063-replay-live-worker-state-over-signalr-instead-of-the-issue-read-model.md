---
reaches: module-contract
---

# Replay Live Worker State over SignalR Instead of the Issue Read Model

## Context

Issue cards render facts that belong to a running worker — how recently the container produced output, and what the agent is currently doing.
These are owned by the Workers module and pushed over `WorkerHub`, while the card itself is driven by `IssueSummary` from the Issues module.
Because the push is the only delivery path, a page reload leaves the card with no value until the next broadcast, which may be minutes away for a quiet worker.

## Decision

Live worker state stays inside Workers and reaches the dashboard entirely over SignalR.
After every connect the client invokes `WorkerHub.GetCurrentActivity`, which returns the current activity of every active run, and replaces what it holds with the result — so a connecting or reconnecting client recovers the same state a long-lived client already holds, and a run that ended while it was disconnected is dropped at once.
`IssueSummary` gains no worker-liveness fields, and Workers exposes no additional cross-module query for them.

Consistent with that stance, `ActiveRun` keeps only what answers "what is true right now" — the last observed activity time — rather than an accumulated history. A worker holds no provider token and pushes nothing during its run ([ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md)), so there is no live branch state to carry; the branch commit count is recorded once, when Foundry publishes the run.

## Considered Options

Adding the commit count to `IssueSummary` was rejected.
It delivers the value in the same payload as the issue list (no handshake flash) but permanently couples a Workers fact into the Issues read model, fixes the reload gap for one field while leaving the identical defect on the activity timestamp, and requires a cross-module query plus edits to three duplicated projections.

## Consequences

A card can render for the duration of the SignalR handshake before its live values appear; the values are absent, never wrong.
Any future live worker fact reaches the UI through the same replay path with no Issues-module change.
Because no commit history is retained, a per-commit activity timeline would have to be sourced from the provider rather than from stored markers.

## Revisions
- 2026-11-12 — `ActiveRun` no longer carries a live head SHA or branch commit count: workers push nothing mid-run, and the count is recorded at publish time (#839).
- 2026-11-13 — Replay moved from a server push in `OnConnectedAsync` to a `GetCurrentActivity` hub method the client invokes after each connect, replacing its state with the result (#927).
