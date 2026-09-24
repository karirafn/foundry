# Per-Repository Capacity Is Enforced in Candidate Selection

## Context

A monitored repository must be limited to a small number of concurrent workers (default 1) so one repository cannot hold every global slot. The obvious home for that check is the dispatch gate in `WorkerDispatchService.cs:143-153`, beside the global `MaxConcurrent` check — but the gate cannot see a repository. Dispatch capacity is published as an anonymous token: `WorkerCapacityAvailable` carries only a `WorkerRunId` (`WorkerCapacityAvailable.cs:5`), and which repository spends that token is decided afterwards, in the Issues module, by `DispatchCandidateSelector`. Of the three slot-occupying states, only `ActiveRun` carries a `MonitoredRepositoryId`; `DispatchReservation` and `StartingRun` do not.

A per-repository cap also separates two things that have so far been one function. Today the dispatcher and the dashboard share `DispatchOrderKey`, so queue order and claim order are identical — the property [ADR 0025](0025-shared-dispatch-order.md) exists to hold and that [ADR 0067](0067-dashboard-reconciles-queue-order-rather-than-deriving-it.md) relies on when it treats the server array order as authoritative. Once a repository can be at its cap, the highest-ordered queued issue is not necessarily the next one claimed.

## Decision

Enforce the per-repository cap in `DispatchCandidateSelector`, not at the Workers-side dispatch gate. The limit travels to the selector on the existing Monitoring-to-Issues contract, `EligibleRepository`, which both the selector and the dashboard query already fetch. The in-flight count is taken from the Issues module's own state — `InProgressIssue` and `RevisionInProgressIssue` per repository — which `IssueClaimer.ClaimAsync` writes in the same transaction as the claim, so there is no window in which a claimed issue is uncounted.

Claim order becomes a capacity-aware walk over `DispatchOrderKey` order, carrying each repository's remaining headroom: an issue whose repository has headroom takes the next place and decrements it; an issue whose repository is saturated defers to the following pass. That ordering is the single shared artifact, replacing the shared comparison key in that role — the dashboard renders it and derives Queue Position by index, and the selector takes its head. The two still cannot disagree.

## Considered Options

**Propagate `MonitoredRepositoryId` onto `DispatchReservation` and `StartingRun` and enforce Workers-side.** This is the consolidation [ADR 0069](0069-dispatch-capacity-held-by-a-durable-slot-reservation.md) names as "the better design if those gates ever consolidate, and this reservation table is what would then be removed". It requires the repository to be chosen before the reservation is minted, which relocates claiming into Workers or exposes it through Contracts — a rewrite of the dispatch protocol rather than an addition to it. Rejected as disproportionate to a capacity cap; the trigger condition ADR 0069 anticipated is now met, so this remains the shape a future consolidation should take.

**Expose a per-repository occupancy query from Workers.Contracts.** Inverts the current dependency direction, and still undercounts: reservations and starting runs carry no repository identity.

**Add a repository-saturation component to `DispatchOrderKey` instead of the walk.** Correct at a limit of 1, wrong at limits of 2 or more, because a sort key sees each issue independently and cannot account for headroom consumed by an earlier issue of the same repository.

## Consequences

The cap constrains which issue is claimed but not whether a capacity token is minted. When every repository with queued work is at its cap, the dispatcher still reserves a slot each tick and the selector releases it via `ClaimSkipped` — the same reserve-then-release path an empty queue already takes, now reached more often.

Lowering a repository's limit below its current in-flight count does not preempt running workers; the repository drains to the new limit as its workers finish.

`Next up` remains truthful with respect to capacity, but not with respect to dispatch-info resolution: `DispatchCandidateSelector` also requires a resolvable `RepositoryDispatchInfo` and the dashboard query does not check that. This predates the decision and is unchanged by it.
