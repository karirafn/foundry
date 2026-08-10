# Separate Durable Outcome-Report Events from Ephemeral Broadcast Events

## Context

The image-build refactor needed two distinct event roles: (1) cross-module writes where the consumer must reliably update persistent state, and (2) SignalR pushes where the consumer has no durable side-effect.
Using a single event type for both would either force every broadcast through the outbox (adding unnecessary persistence overhead and retry logic for pure UI notifications) or risk losing state-change events on transient failures.

## Decision

Two categories of integration events are used, with different delivery guarantees:

- **Durable report events** (`ImageBuildRequested`, `ImageBuildSucceeded`, `ImageBuildOutcomeFailed` in `Workers.Contracts`) — published via `IIntegrationEventDispatcher`, persisted to the outbox, delivered at-least-once by `OutboxRelayService`, and deduplicated in the inbox before handler invocation. The Settings module's handlers consume these and write `GlobalSettings`.
- **Ephemeral broadcast events** (`ImageBuildStarted`, `ImageBuildCompleted`, `ImageBuildFailed` in `Settings.Contracts`) — raised directly on the `GlobalSettings` aggregate after each transition and delivered synchronously via `IIntegrationEventProcessor`, bypassing the outbox entirely. Workers broadcast handlers push them over SignalR with no durable consumer.

The split is enforced by the event namespaces: cross-module writes live in the producer's Contracts, ephemeral broadcasts live in the owning module's Contracts.

## Consequences

Adding a new durable side-effect on an image-build outcome requires a handler registered against the Workers Contracts event — no change to `WorkerImageRebuildService`.
Adding a new real-time UI signal requires a handler registered against the Settings Contracts broadcast event — no change to the Settings handlers.
The two concerns scale independently.
