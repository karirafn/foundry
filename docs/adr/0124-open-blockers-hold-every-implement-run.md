---
reaches: structure
---

# Open Blockers Hold Every Implement Run

## Context

Only fresh and triage candidates were re-checked for open blockers. A revision or continuation on an existing branch dispatched while its issue had open blockers, so work was rebuilt against dependencies that did not exist yet, and a closed pull request did not stop a queued revision.

## Decision

An issue with open blockers starts no implement run.

- `RevisionQueued` and `ContinuationQueued` with open blockers become `RevisionBlocked` and `ContinuationBlocked`; each unblocks only to its own queued state (as [ADR 0102](0102-triage-runs-once-blockers-clear.md) does for triage). The dependency pass and the claim-time re-check both apply it.
- Runs in progress, rebase, checks, review and merge are not interrupted.
- A closed pull request fails `RevisionQueued` and `RevisionBlocked` as continuable (`PrClosed`), like every other pull-request state.

## Considered Options

- **Gate merge too:** rejected; a person controls merge whenever person feedback exists.
- **Skip held issues at selection without a state:** rejected; the dashboard would show held work as queued.
- **A resume target on `BlockedIssue`:** rejected; it reverses ADR 0102's one blocked state per target.

## Consequences

- A blocker cycle holds a revision until a link is removed, as for fresh work.
