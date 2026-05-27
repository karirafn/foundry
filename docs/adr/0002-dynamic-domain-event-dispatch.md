# Dynamic Dispatch for Domain Events

## Context

The domain event dispatcher needs to invoke typed `IDomainEventHandler<TEvent>` instances without knowing the concrete event type at compile time, because events are collected as `IEnumerable<IDomainEvent>` and resolved from DI using `typeof(IDomainEventHandler<>).MakeGenericType(event.GetType())`.

## Decision

We use a `dynamic` cast — `((dynamic)handler).HandleAsync((dynamic)event, cancellationToken)` — rather than reflection via `MethodInfo.Invoke()` or source generators. The `dynamic` approach is readable, requires no boilerplate, and the runtime binder resolves the correct generic overload. Source generators were unnecessary complexity, and `MethodInfo.Invoke()` adds noise without benefit. Native AOT incompatibility with `dynamic` is not a concern because Foundry targets standard ASP.NET Core.

## Considered Options

- **`MethodInfo.Invoke()`** — rejected because it requires boxing, error handling for `TargetInvocationException`, and is harder to read than the `dynamic` equivalent.
- **Source generators** — rejected because the infrastructure is not large enough to justify the build complexity.
