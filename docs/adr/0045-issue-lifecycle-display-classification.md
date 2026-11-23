---
reaches: module-contract
---

# Classify issue lifecycle display groups by "is a worker running"

## Context

The dashboard groups active issue states into four display buckets.
Membership was inherited without a stated rule and had drifted into contradictions: `continuation_queued` sat in "In progress" while the structurally-identical `revision_queued` sat in "Waiting", and `unchanged` sat in "Resolved" despite requiring manual resolution.

## Decision

Adopt one rule and classify every state by it: Needs you = requires a user action; Queued = waiting for a worker, progresses with no user action; In flow = a worker is running now, or Foundry's own code is carrying the issue forward; Resolved = done.
This puts `continuation_queued` with the other queued tiers, puts `unchanged` in Needs you (manual resolution), and aligns the server `IssueStateRegistry` Active/Resolved partition with the frontend (`unchanged` is Active).
The dead `ineligible` issue state is removed from the frontend union and all display maps.

The classification is declared by each server state type — `Issue.NeedsAction` and `Issue.AwaitsDispatch` — and served on `IssueSummary`, so the dashboard places an issue from the facts the server declares rather than from a frontend list of states.

## Consequences

Future states are classified by the rule rather than guessed; a new state type cannot compile without declaring its placement.
`unchanged` now loads in the always-on active query (unpaginated — accepted, low volume).
The frontend↔server resolved-wire-name agreement is enforced only by mirrored literals plus a frontend contract test, not a runtime check.

## Revisions
- 2026-11-13 — Display groups became Needs you / Queued / In flow / Resolved, placed from server-declared `NeedsAction`/`AwaitsDispatch` instead of frontend state maps, and the frontend `serverIndex` tiebreaker was deleted with the active issue list (#880, #896).
