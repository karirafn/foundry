# Recover stranded starting runs via the failure bridge

## Context

`IssueClaimedHandler` commits a `StartingRun` row before it creates the branch, builds the container spec, or calls `StartAsync`.
Any death in that window — a Docker daemon crash, a host restart, or an `IssueClaimed` outbox message exhausting its retries and dead-lettering — leaves the run in `starting` and its issue in `in_progress` forever.
Nothing reads `StartingRun` rows for recovery: the startup reconcile loads `Set<ActiveRun>()` only, and the timeout watchdog keys off `StartedAt`, which only `ActiveRun` carries.
Recovery required editing `foundry.db` by hand.

## Decision

A periodic sweep fails `StartingRun` rows older than a fixed threshold, and the existing failure bridge carries the issue the rest of the way.

`StartingRun.Fail(reason)` already raises `WorkerRunFailed` with `BranchName: null`, which `WorkerRunFailedHandler` already turns into `InProgressIssue.MarkFailed(...) → FailedIssue`, which `RetryIssue` already accepts and the dashboard already offers a Retry button for.
The sweep therefore adds one producer to a path that is already built and tested, rather than a new route through the state machine.

`InProgressIssue` keeps its forward-only shape — no `Requeue()`, no backward transition to `QueuedIssue` — and `RetryIssue` keeps rejecting `in_progress` with a conflict.

## Considered Options

**Add `InProgressIssue.Requeue() → QueuedIssue` and widen `RetryIssue` to accept `in_progress`.**
This is what the originating issue proposed.
Rejected: it duplicates a working path, and it opens a backward edge in a state machine whose forward-only shape is what makes its transitions checkable at compile time.
The set of retryable states is already triplicated (the handler switch, a hand-written error string, and the frontend's `RETRYABLE_STATES`), so widening it means editing three places to reach a state the bridge already reaches with none.

**Adopt the orphaned container into a reconstructed `ActiveRun`.**
Rejected: not available. `StartingRun` carries only `Id`, `IssueId`, and `CreatedAt`; `ActiveRun.FromStarting` needs `BranchName` and `MonitoredRepositoryId`, which live only in the in-memory dispatch payload that died with the handler.
Adoption would require persisting them on the `starting` row — a schema change well beyond the recovery this decision is about.

## Consequences

Recovery surfaces as a visible `FailedIssue` under "Needs attention" with a `container_error` reason, not a silent requeue.
The operator sees that something went wrong and chooses to retry, following the precedent set by [ADR 0014](0014-remove-immediate-requeue-always-pause.md) (*Remove Immediate Requeue, Always Pause on Usage Limit*).
Auto-retry does not apply: `TransientRetryService` filters on `transient_api_error`, so a `container_error` failure parks until the operator acts.

Reversing this decision — adding the backward transition after all — costs more than adding it now would have, because the bridge path will by then be the documented and tested one.
That cost is accepted deliberately: the forward-only state machine is a property worth paying to keep.
