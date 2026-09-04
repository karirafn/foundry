# Dispatch Capacity Is Held by a Durable Slot Reservation Rather Than an In-Flight Event

## Context

`WorkerCapacityAvailable` carried authorization to claim an issue, but the authorization existed only as a
message in flight: `WorkerDispatchService` checked slot occupancy against `MaxConcurrent` before publishing
(`WorkerDispatchService.cs:143-153`) and nothing re-checked it on receipt. Nothing in the database recorded
that a slot had been promised, so consecutive dispatch ticks could each publish a fresh authorization while
the previous one was still undelivered.

A stalled outbox relay on 2026-08-18 accumulated roughly 570 such events; the drain claimed all five queued
issues against `max_concurrent=1`. Two properties of the existing machinery make a claim-time re-read of
occupancy insufficient on its own. The relay orders by `occurred_at`
(`OutboxDbContextExtensions.cs:27`), so every backlogged authorization pre-dates every `IssueClaimed` the
drain itself produces; and a `StartingRun` row — the thing occupancy counts — is created only once
`IssueClaimed` is relayed (`IssueClaimedHandler.cs:44-46`). Occupancy therefore reads zero for the whole
drain, and a claim-time recount would have permitted all five claims.

## Decision

Slot occupancy is held by a durable `DispatchReservation` row in the Workers module, keyed on `WorkerRunId`,
created inside the dispatch tick's existing capacity-check transaction and counted by
`GetSlotOccupancyCountAsync` alongside `starting` and `active` runs. The authorization event still carries
only the `WorkerRunId`; the reservation is what the id now refers to.

`IssueClaimedHandler` consumes the reservation by deleting it and adding the `StartingRun` under the same id
in one transaction, so occupancy never dips between the two. A stale authorization is therefore one whose
reservation no longer exists, and needs no timestamp comparison to detect.

Because a held reservation suppresses further publication, it needs an exit on every path. The Issues module
publishes `ClaimSkipped(WorkerRunId)` on each non-`Selected` selection outcome and a Workers-side handler
deletes the reservation; a dedicated `StaleReservationService` deletes reservations older than two minutes as
the backstop for host crashes and dead-lettered rows, which no event will ever cover.

Redelivery is bounded separately: the Issues module skips selection when an issue already carries the event's
`WorkerRunId`. That id persists across every post-claim state, so the guard needs no cross-module read.

## Considered Options

- **Collapse the capacity decision and the claim into one transaction, deleting the event** — the design
  under which every acceptance criterion becomes vacuous rather than satisfied. Rejected because claiming is
  an Issues operation while all four dispatch gates are Workers-side (`ICredentialGate.CanDispatchAsync`,
  dispatch-pause state, image-build status, occupancy — `WorkerDispatchService.cs:116-153`), so the
  transaction requires relocating or Contracts-exposing all four. This becomes the better design if those
  gates ever consolidate, and this reservation table is what would then be removed.
- **Count unpublished `WorkerCapacityAvailable` outbox rows as occupancy** — needs no table and no explicit
  release, since the relay marking a row published is the consumption. Rejected: it makes a domain invariant
  read out of `Foundry.Shared.Infrastructure`'s delivery table, shared by all five modules; and a poison row
  that exhausts `MaxAttempts` is skipped by the relay permanently (`OutboxDbContextExtensions.cs:26`), so it
  would hold its slot forever unless the occupancy query duplicated that filter.
- **A `ReservedRun` TPH variant on `WorkerRun`** — reuses the table and the occupancy query verbatim.
  Rejected: a reservation has no issue, so this forces `IssueId` nullable on the base
  (`WorkerRun.cs:15-21`), pushing an impossible null onto all four existing states, the
  `ix_worker_runs_issue_id` index, and `WorkerRunFailed`'s payload.
- **Re-check occupancy at claim time, counting in-progress issues as well as runs** — rejected: two
  differently-shaped counts must then agree about one invariant, and the claim side derives occupancy from
  state the Workers module owns.
- **Age sweep as the only release path** — rejected on liveness. With an empty queue the tick reserves, the
  handler finds nothing, and the slot sits until the sweep; tightening the threshold to recover faster
  shrinks the margin that stops a sweep from racing a live claim.
- **Fold the reservation sweep into `StaleStartingRunService`** — rejected: that tick opens with
  `ListByLabelAsync` and returns early on any Docker failure (`StaleStartingRunService.cs:59-80`), which
  would disable reservation release exactly when an unreachable daemon is causing reservations to accumulate.

## Consequences

Over-dispatch is prevented at the source rather than caught downstream: with a reservation outstanding, the
gate publishes nothing, so a backlog of authorizations cannot form in the first place.

The reservation is a state that suppresses the work that would clear it, so its two release paths are
load-bearing rather than defensive — `ClaimSkipped` for the ordinary unused-authorization case and the
periodic sweep for the cases no event can reach. The two-minute threshold is a deliberate compromise: wide
against a reserve-to-claim interval normally under five seconds and against `MaxAttempts=10` dead-lettering
at a two-second relay tick, and short enough that a restart with reservations outstanding does not idle
dispatch the way the ten-minute stale-starting-run threshold would.

Costs: one table and one migration; a third `PeriodicBackgroundService` in the Workers module; and a
release-path invariant that any new selection outcome in the Issues module must publish `ClaimSkipped`,
since an outcome added without it leaks a slot until the sweep.
