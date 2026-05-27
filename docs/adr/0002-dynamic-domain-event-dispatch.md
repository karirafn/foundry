# Non-Generic Base Interface for Domain Event Dispatch

## Context

The domain event dispatcher needs to invoke typed `IDomainEventHandler<TEvent>` instances without knowing the concrete event type at compile time, because events are collected as `IEnumerable<IDomainEvent>` and resolved from DI using `typeof(IDomainEventHandler<>).MakeGenericType(event.GetType())`.

## Decision

`IDomainEventHandler<TEvent>` extends a non-generic `IDomainEventHandler` base interface. The base interface declares `HandleAsync(IDomainEvent, CancellationToken)`. The generic interface provides a default interface method implementation that casts the `IDomainEvent` to `TEvent` and delegates to the typed overload. The dispatcher resolves handlers as `IDomainEventHandler<TEvent>` via DI, casts each to `IDomainEventHandler`, and calls `HandleAsync` directly — no `dynamic`, no reflection on `MethodInfo`.

## Considered Options

- **`dynamic` dispatch** — rejected because `dynamic` requires cross-assembly visibility of handler types (`public` access), bypasses compile-time type checking, and is incompatible with Native AOT.
- **`MethodInfo.Invoke()`** — rejected because it requires boxing, error handling for `TargetInvocationException`, and is harder to read than the base-interface approach.
- **Source generators** — rejected because the infrastructure is not large enough to justify the build complexity.
