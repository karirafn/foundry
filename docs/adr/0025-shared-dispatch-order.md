# Shared Dispatch Order for Dispatcher and Dashboard

## Context

The dashboard sorted queued issues newest-first, the opposite of dispatch order (revision → continuation → fresh, then by repository position).
Two independent encodings of tier precedence existed: a `switch` ladder in `WorkerCapacityAvailableHandler` and an implicit oldest-first sort in the list query.
Any change to tier precedence required two coordinated edits, and the dashboard's "next up" label could silently disagree with what the dispatcher would actually claim.

## Decision

Introduce a single `DispatchOrderKey` (`readonly record struct`, `IComparable`) in `Foundry.Modules.Issues/Domain/` that encodes the total order `(TierRank, Position, DetectedAt, Id)`.

`TierRank` is a property on each queued state variant (revision = 0, continuation = 1, fresh = 2) rather than a switch in the handler.
Both consumers use the same key: the dispatcher selects the next claim via min-by-key; the list query sorts the queued subset in memory by the same key after a filtered DB fetch.

In-memory ordering is intentional — `TierRank` is a discriminated-union property over a TPH hierarchy and `Position` is supplied externally from the repository record, making the composite key not SQL-translatable without a significant query-complexity trade-off.
The active queued set is small and bounded by `MaxConcurrentWorkers`, so in-memory sorting is safe.

The dashboard also partitions queued issues: eligible-repository issues (real `Position`) rank above ineligible-repository issues (sentinel `int.MaxValue` position), each retaining its `RepositoryEligibilityStatus` for display.

## Considered Options

- **Client-side sort with Position and tier added to the DTO** — rejected: duplicates dispatch logic in TypeScript, drifts independently, and the displayed "next up" marker would lie whenever the client's sort diverged from the server's actual claim order.
- **Add the ordered query only, leave the dispatcher's switch ladder** — rejected: two encodings of tier precedence reintroduce drift; a new tier still requires two edits.
- **Order all queued issues purely by key, badge ineligible in place** — rejected: an ineligible issue at TierRank 0 would sort to the top and be badged "Next up" even though it cannot be claimed, misleading the operator.

## Consequences

Adding a new queued tier (e.g. a future `HighPriorityQueuedIssue`) requires only: a new state variant with the appropriate `TierRank` value.
Both the dispatcher and the dashboard pick it up automatically — no secondary edit needed.

The in-memory ordering assumption holds while the active queued set is small.
If Foundry is ever deployed at a scale where the queued set is unbounded, the ordering should move into the database query, which would require making `TierRank` and `Position` SQL-visible (e.g. stored columns or a more complex join).
