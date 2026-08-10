# Invert Image-Build Persistence and Broadcast Ownership via Events

## Context

`WorkerImageRebuildService` originally owned three responsibilities: building the Docker images, mutating `GlobalSettings` (begin/complete/fail), and directly broadcasting `SystemNotification` SignalR events.
This created a cross-module dependency from Workers into the Settings domain layer, made the service hard to test (required a real database and a broadcaster spy), and violated the principle that aggregate writes belong to the module that owns the aggregate.

## Decision

Ownership of `GlobalSettings` mutation and SignalR broadcast was inverted through a two-event-type split:

- **Workers→Settings report events** (`ImageBuildRequested`, `ImageBuildSucceeded`, `ImageBuildOutcomeFailed`) — published via `IIntegrationEventDispatcher` (outbox) from `WorkerImageRebuildService`. Settings handlers consume them, call the aggregate transition method, persist the change, and then direct-deliver the resulting broadcast events.
- **Settings broadcast events** (`ImageBuildStarted`, `ImageBuildCompleted`, `ImageBuildFailed`) — raised ephemerally by the `GlobalSettings` aggregate and delivered via `IIntegrationEventProcessor` (no outbox row); Workers broadcast handlers turn them into `SystemNotification` SignalR pushes.

`WorkerImageRebuildService` now reads build args via `IGlobalSettingsQueries` and checks row-existence via `GetSettingsAsync`, with no reference to the Settings domain layer at all.

## Considered Options

Keeping `WorkerImageRebuildService` as the aggregate writer was rejected: it couples two modules at the domain layer, leaks persistence concerns into a background service, and requires integration-test infrastructure (real DB) for unit tests that should only need image-build logic.

## Consequences

`WorkerImageRebuildService` unit tests now use a stub `IGlobalSettingsQueries` and a capturing `IIntegrationEventDispatcher` — no SQLite setup or broadcaster spy.
The Settings module owns all `GlobalSettings` writes, consistent with aggregate-ownership boundaries.
