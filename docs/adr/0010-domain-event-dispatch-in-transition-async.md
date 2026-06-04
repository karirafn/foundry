# Domain Event Dispatch in TransitionAsync

## Context

State transitions in the Issues module raise domain events inside entity transition methods, but dispatch to handlers is manual — each handler must remember to call `dispatcher.DispatchAsync(entity.DomainEvents)` after `TransitionAsync`. 11 of 16 call sites were missing this call, causing silent SignalR broadcast failures. The manual dispatch pattern makes the wrong path (forgetting to dispatch) the easy path.

## Decision

`TransitionAsync` accepts an `IDomainEventDispatcher` parameter and dispatches domain events from the source entity automatically after the transaction commits. A new `IDomainEventSource` interface in `Foundry.Shared` exposes `DomainEvents` and `ClearDomainEvents()` — `AggregateRoot<TId>` implements it. The `TFrom` constraint on `TransitionAsync` requires `IDomainEventSource`, making it a compile error to transition an entity that cannot source domain events.

## Considered Options

- **SaveChangesInterceptor** — rejected because `TransitionAsync` calls `SaveChanges` twice within a transaction, and the interceptor would fire on the wrong save.
- **Handler pipeline decorator** — rejected because some handlers perform multiple transitions and non-transition saves, making timing ambiguous.
- **Keep manual dispatch** — rejected because it is the root cause of the bug. The pattern violates pit-of-success principles.

## Consequences

Every `TransitionAsync` call site must pass an `IDomainEventDispatcher` — this is a breaking change to the method signature. Handlers that previously dispatched manually have that code removed. Handlers that previously forgot to dispatch now dispatch automatically. The change affects both Issues and Workers modules.
