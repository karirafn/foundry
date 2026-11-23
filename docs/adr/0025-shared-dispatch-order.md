---
reaches: hidden-invariant
---

# Shared Dispatch Order for Dispatcher and Dashboard

## Context

The dashboard sorted queued issues newest-first, the opposite of dispatch order (revision → continuation → fresh, then by repository position).
Two independent encodings of tier precedence existed: a `switch` ladder in `WorkerCapacityAvailableHandler` and an implicit oldest-first sort in the list query.
Any change to tier precedence required two coordinated edits, and the dashboard's "next up" label could silently disagree with what the dispatcher would actually claim.

## Decision

Introduce a single `DispatchOrderKey` (`readonly record struct`, `IComparable`) in `Foundry.Modules.Issues/Domain/` that encodes the total order `(TierRank, Position, IssueNumber, Id)`.

`TierRank` is a property on each queued state variant (revision = 0, continuation = 1, fresh = 2) rather than a switch in the handler.
Both consumers use the same key: the dispatcher selects the next claim via min-by-key; the list query sorts the queued subset in memory by the same key after a filtered DB fetch.

In-memory ordering is intentional — `TierRank` is a discriminated-union property over a TPH hierarchy and `Position` is supplied externally from the repository record, making the composite key not SQL-translatable without a significant query-complexity trade-off.
The active queued set is small and bounded by `MaxConcurrentWorkers`, so in-memory sorting is safe.

Within a repository and tier the lowest issue number is dispatched first, because the issue number reflects the order the author intended, while detection time reflects only when Foundry noticed the issue. Tier still outranks issue number, so revisions and continuations finish before fresh work begins. `(MonitoredRepositoryId, IssueNumber)` is unique, so `Id` only breaks ties between issues whose repositories share a sentinel position. An issue that is unblocked, reopened or labelled late therefore goes ahead of newer queued work in the same repository and tier; newly filed issues always have higher numbers, so this cannot starve queued work.

The dashboard also partitions queued issues: eligible-repository issues rank above ineligible-repository issues, each retaining its `RepositoryEligibilityStatus` for display. Both partitions order by each repository's real `Position`, so a repository's issues stay together and in number order; the `int.MaxValue` sentinel position is only the fallback for a repository with no display info.

## Considered Options

- **Client-side sort with Position and tier added to the DTO** — rejected: duplicates dispatch logic in TypeScript, drifts independently, and the displayed "next up" marker would lie whenever the client's sort diverged from the server's actual claim order.
- **Add the ordered query only, leave the dispatcher's switch ladder** — rejected: two encodings of tier precedence reintroduce drift; a new tier still requires two edits.
- **Order all queued issues purely by key, badge ineligible in place** — rejected: an ineligible issue at TierRank 0 would sort to the top and be badged "Next up" even though it cannot be claimed, misleading the operator.
- **Order same-repository issues by `DetectedAt`** — rejected: it orders by when Foundry noticed an issue rather than by author intent, and ties fall back to `Id`, a random `Guid`.
- **Issue number above tier, `(Position, IssueNumber, TierRank, Id)`** — rejected: a revision on a high-numbered issue would wait behind fresh low-numbered work, and repository position would outrank tier globally.

## Consequences

Adding a new queued tier (e.g. a future `HighPriorityQueuedIssue`) requires only: a new state variant with the appropriate `TierRank` value.
Both the dispatcher and the dashboard pick it up automatically — no secondary edit needed.

The in-memory ordering assumption holds while the active queued set is small.
If Foundry is ever deployed at a scale where the queued set is unbounded, the ordering should move into the database query, which would require making `TierRank` and `Position` SQL-visible (e.g. stored columns or a more complex join).

## Revisions
- 2026-11-05 — `IssueNumber` replaces `DetectedAt` in the key, and the dashboard orders ineligible-repository issues by real `Position` instead of the `int.MaxValue` sentinel (#1226)
