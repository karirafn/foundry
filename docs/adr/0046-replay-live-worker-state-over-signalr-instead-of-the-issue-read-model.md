# Replay Live Worker State over SignalR Instead of the Issue Read Model

## Context

Issue cards render facts that belong to a running worker — how recently the container produced output, and how many commits sit on the issue's branch.
These are owned by the Workers module and pushed over `WorkerHub`, while the card itself is driven by `IssueSummary` from the Issues module.
Because the push is the only delivery path, a page reload leaves the card with no value until the next broadcast, which may be minutes away for a quiet worker.

## Decision

Live worker state stays inside Workers and reaches the dashboard entirely over SignalR.
`WorkerHub.OnConnectedAsync` replays the current activity payload for every active run, so a connecting or reconnecting client recovers the same state a long-lived client already holds.
`IssueSummary` gains no worker-liveness fields, and Workers exposes no additional cross-module query for them.

Consistent with that stance, `ActiveRun` stores only the branch state needed to answer "what is true right now" — the observed head SHA (for change detection) and the branch commit count — rather than an accumulated history of commit markers.
The count is derived from a provider merge-base comparison against the default branch, so it is a projection of provider truth, not a Foundry-maintained tally.

## Considered Options

Adding the commit count to `IssueSummary` was rejected.
It delivers the value in the same payload as the issue list (no handshake flash) but permanently couples a Workers fact into the Issues read model, fixes the reload gap for one field while leaving the identical defect on the activity timestamp, and requires a cross-module query plus edits to three duplicated projections.

## Consequences

A card can render for the duration of the SignalR handshake before its live values appear; the values are absent, never wrong.
Any future live worker fact reaches the UI through the same replay path with no Issues-module change.
Because no commit history is retained, a per-commit activity timeline would have to be sourced from the provider rather than from stored markers.
