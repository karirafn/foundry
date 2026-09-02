# Dashboard Reconciles Queue Order Rather Than Deriving It

## Context

[ADR 0025](0025-shared-dispatch-order.md) made `DispatchOrderKey` the single encoding of dispatch order for both the dispatcher and the dashboard list query, but that guarantee ends at the HTTP response boundary.
The client receives the order only as the array order of one response and then patches that array per-issue from `IssueUpdated` events, so any live queue transition leaves the rendered order — and the `Next up` marker derived from it — disagreeing with the order the dispatcher would actually claim in.
Dispatch order is a function of every queued row plus repository `Position` and eligibility, so a single-issue event can never carry enough information to repair it.

## Decision

The client treats server array order as the only source of queue order and **reconciles** it by refetching the ordered list, rather than deriving order locally or having the server push an order.

- Every `IssueUpdated` event schedules a debounced reconcile (`GET /api/issues`) alongside the counts refetch that already runs on that path, with no per-state trigger classification.
- A low-frequency safety-net timer schedules the same reconcile, covering the order inputs that raise no issue event at all — repository `Position` moves and eligibility changes.
- The reconcile fetch carries a latest-wins request token, so a late response cannot install a stale order.
- The displayed Queue Position ordinal is a 1-based index over the rendered dispatchable-queued array, never a value transported from the server.

`IssueSummary` therefore gains no ordering fields, and `DispatchOrderKey` remains the only encoding of the order.

## Considered Options

- **Add `tierRank` and `position` to `IssueSummary` and sort client-side** — rejected for the reason 0025 already recorded: it puts a second copy of the comparator in TypeScript, free to drift. It also fails independently here, because `position` would only refresh when an issue event happened to arrive.
- **Broadcast a `QueueOrderChanged` event carrying the ordered ids** — rejected: a second ordering contract that must be raised from every site that changes an order input. Forgetting one compiles clean and leaves confidently-wrong ordinals on screen, which is the same silent-drift failure this ADR exists to remove.
- **Per-issue `queuePosition` field on the summary** — rejected: a global property delivered per-issue. One issue's event cannot correct the ranks its own arrival shifted, and the transported number could disagree with the sequence actually rendered.
- **Ephemeral broadcast from `MoveRepository` and the eligibility recheck instead of the timer** — rejected: exact and immediate, but carries the same forgettable-trigger weakness as the option above, at the cost of a Monitoring-module change and new event plumbing. A repository reorder is a deliberate, infrequent operator action for which bounded staleness is acceptable.
- **Patch the local array into correct position on each event** — rejected: requires the comparator client-side, so it collapses into the first option.

## Consequences

A future input to dispatch order needs no client wiring — the safety-net reconcile covers it without anyone remembering to raise anything. The cost is bounded staleness on inputs that raise no event, and one idle `GET /api/issues` per timer interval.

The ordinal cannot disagree with the sequence the operator sees, because it counts the array the cards are rendered from.

A failed reconcile leaves the rendered order possibly stale while looking authoritative, so a failed reconcile is disclosed in the UI and cleared by the next successful one. Its automatic exit is the next event-driven or timer-driven reconcile; the SignalR reconnect path performs a full reload.

Ordering remains in memory over the bounded queued set, per 0025 — the reconcile changes when the ordered list is fetched, not how it is ordered.
