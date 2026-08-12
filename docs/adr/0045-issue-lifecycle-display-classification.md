# Classify issue lifecycle display groups by "is a worker running"

## Context

The dashboard groups active issue states into four display buckets (In progress, Needs attention, Waiting, Resolved).
Membership was inherited without a stated rule and had drifted into contradictions: `continuation_queued` sat in "In progress" while the structurally-identical `revision_queued` sat in "Waiting", and `unchanged` sat in "Resolved" despite requiring manual resolution.

## Decision

Adopt one rule and reclassify every state by it: In progress = a worker is running now; Waiting = waiting for a worker, progresses with no user action; Needs attention = requires a user action; Resolved = done.
This moves `continuation_queued` to Waiting (it is a queued tier), `unchanged` to Needs attention (manual resolution), and aligns the server `IssueStateRegistry` Active/Resolved partition with the frontend (`unchanged` becomes Active).
The dead `ineligible` issue state is removed from the frontend union and all display maps.
"In progress" is now exactly `LIVE_STATES`, collapsing the issue-list divider onto the bucket boundary.

## Consequences

Future states are classified by the rule rather than guessed.
`unchanged` now loads in the always-on active query (unpaginated — accepted, low volume).
The frontend↔server resolved-wire-name agreement is enforced only by mirrored literals plus a frontend contract test, not a runtime check.
The `serverIndex` dispatch-order tiebreaker in `sortedIssues` becomes more load-bearing (the full queued chain is now contiguous in one bucket) and must stay.
