# Custom CQRS Dispatch over MediatR

## Context

Foundry needed a command and query dispatch mechanism to implement the CQRS pattern across its vertical slices.

## Decision

We implemented custom `ICommandHandler<TCommand, TResult>` and `IQueryHandler<TQuery, TResult>` interfaces with DI-based wiring instead of adopting MediatR. The required functionality — dispatch, validation decoration via Scrutor's `.Decorate<>()`, and domain event dispatch — amounts to roughly 30 lines of code. Introducing MediatR would add a framework dependency, pipeline abstractions, and conventions that exceed what the project requires.

## Considered Options

- **MediatR** — rejected because the project's dispatch needs are simple, and a dependency adds upgrade risk and coupling to third-party conventions.
