---
reaches: hidden-invariant
---
# The Server Declares Each Issue's Lifecycle Path, and the Dashboard Renders It

## Context

The board showed where an issue sat (a band and a stage chip) but not its path: which steps it passed, which come next, and which are code-run.
Whether a green pull request merges or waits for a person depends on repository settings and `HasPersonFeedback`, which the client cannot see.
Rejected: a client-side step catalogue working out off and skipped steps from repository settings, which would miss `HasPersonFeedback`.
Also rejected: deriving everything from `state`, `codeStep` and `publishPhase`, which cannot show Prepare.

## Decision

Each issue state type declares its `LifecycleStep`, so a new state cannot compile without a place on the rail.
The summary serves `lifecycle[]`: every step in order, with its actor (Claude, Foundry code, person) and status (done, current, upcoming, off, skipped).
The current step's phase comes from the state and the run: Prepare is the run's `starting` phase, served by the query that already serves the publish phase.
`merge_pending` moves from Implement into the final stage, renamed from Awaiting review to Merge. The rail draws neither stages nor queue waits: a current queue wait shows on the node after it, which keeps every rail at one column per node.
A client renders a step it does not know as a generic node from the served actor and status, and never drops the issue.

## Consequences

Adding a state or phase means placing it in `lifecycle[]` on the server; the dashboard needs copy for it but no rule.
The stage token rename and the client's narrowing must land together.
The current entry of `lifecycle[]` is the only served source for the issue's current step: the summary carries no `codeStep`, and step text (the step chip, the catch-up badge) is read from that entry.

## Revisions
- 2026-11-24 — `codeStep` removed; every reader takes the current step from `lifecycle[]` (#1245)
- 2026-11-24 — the rail no longer draws stage brackets or queue-wait dots; a current wait folds into the next node (#1262)
