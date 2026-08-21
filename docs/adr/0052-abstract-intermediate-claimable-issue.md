# Abstract intermediate QueuedIssue collapses three-way queued-variant unions

## Context

`FreshQueuedIssue`, `RevisionQueuedIssue`, and `ContinuationQueuedIssue` share dispatch behavior — each carries a tier rank, a branch name, a dispatch context, and a `Claim(workerRunId)` transition.
Before this change, that commonality was duplicated across three near-identical `ClaimXxxAsync` methods in `WorkerCapacityAvailableHandler`, a three-arm tier switch inside each, and three-way type unions in `IsRestingState`, `IsQueuedVariant`, `GetUntrackableIssueNumbersAsync`, and `DispatchOrderKey.For`.
The handler had accumulated four collaborator dependencies plus in-line selection and claim logic, exceeding the 4-dependency tripwire and the ~150-line class limit.

## Decision

Introduce `abstract QueuedIssue : Issue` as an explicit intermediate in the Issue TPH hierarchy, carrying the members shared by every queued state:

- `TierRank` (abstract, computed) — dispatch-priority rank, overridden by each concrete variant.
- `DispatchBranchName` (abstract, computed) — branch name the worker operates on.
- `Context` (abstract, computed) — the `DispatchContext` union value (Fresh / Revision / Continuation), assembled on the aggregate, not in the handler.
- `Claim(Guid workerRunId)` (abstract) — transitions to the in-progress state with a covariant return override on each concrete type.

Every three-way union in the codebase collapses to `is QueuedIssue` or `OfType<QueuedIssue>()`.
`DispatchOrderKey.For` is narrowed to accept `QueuedIssue` directly, deleting its runtime `InvalidOperationException` guard — the invalid case is now unrepresentable at compile time.

The handler is decomposed into two collaborators:

- **`DispatchCandidateSelector`** (3 dependencies) — resolves eligible repositories, sorts candidates by `DispatchOrderKey`, memoizes dispatch-info resolution per repository within a tick, and falls through unresolvable candidates rather than aborting the tick.
- **`IssueClaimer`** (3 dependencies) — executes the atomic state transition, integration-event dispatch, and persistence for the selected `DispatchCandidate`.

`WorkerCapacityAvailableHandler` becomes a 2-dependency orchestrator that delegates to both collaborators and logs terminal outcomes.

**EF Core registration is load-bearing.**
EF Core 10 omits an abstract intermediate from the model unless it is explicitly registered.
Without registration, `OfType<QueuedIssue>()` and `is QueuedIssue` in translated LINQ queries throw `InvalidOperationException` at query time — not at model build and not at compile time.
`QueuedIssue` is registered via a dedicated `IEntityTypeConfiguration<QueuedIssue>` with `HasBaseType<Issue>()`.
No `HasValue<T>()` discriminator entry is added — EF assigns an unused default discriminator value and `HasDiscriminator(...).IsComplete(true)` stays valid through the concrete leaves.
Computed get-only members (`TierRank`, `DispatchBranchName`, `Context`) are not mapped and require no `Ignore()`.

**In-memory sort.**
`OrderBy(TierRank)` does not translate to SQL because `TierRank` is a computed, unmapped property.
Sorting the bounded queued set stays in memory, consistent with the decision in ADR 0025 to keep the queued candidate list small enough that an in-memory sort is the correct call.

## Consequences

Dispatch behavior lives on the aggregates that own the state; the handler orchestrates via typed collaborators with no per-tier switches.
Three-way type unions cannot drift — a new queued variant that does not extend `QueuedIssue` is immediately excluded from dispatch at query time rather than silently absent.
`DispatchOrderKey.For` accepts only `QueuedIssue`, so the former unreachable-branch guard is deleted; the compiler flags any call site that passes a non-queued issue.

New abstract intermediates in this TPH hierarchy carry the same explicit-registration obligation: omitting `modelBuilder.Entity<T>()` for an abstract intermediate is a silent correctness defect that surfaces only at runtime under a translated query.
