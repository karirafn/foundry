# Background Service Polling over Domain Event-Driven Dispatch

## Context

Worker containers need to be dispatched when an issue enters the `QueuedIssue` state.
Options were: (a) a domain event handler that reacts to `IssueClaimed` / `IssueQueued` events and dispatches immediately, (b) a `BackgroundService` that polls on a periodic tick.

## Decision

Use a single `WorkerDispatchService` (`BackgroundService`) that polls for `QueuedIssue` records on a periodic timer.
Each tick runs inside a single scoped transaction, which provides a natural concurrency control point — only one goroutine claims and dispatches at a time, preventing double-dispatch without distributed locking.
The polling loop also handles the full lifecycle (dispatch, monitoring, timeout, report ingestion, reconciliation) in one place, making the coordination logic explicit and easy to reason about.

## Considered Options

- **Domain event handler** — rejected because event handlers are per-event and concurrent; preventing double-dispatch across handlers would require an external lock or idempotency guard, adding complexity without benefit.

## Consequences

Dispatch latency equals the tick interval (10 seconds) rather than being immediate.
This is acceptable for a developer-automation tool where a few seconds of latency are imperceptible.
