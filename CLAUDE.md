# CLAUDE.md

## Project Overview

Foundry is a containerized service that monitors repositories across multiple providers (GitHub, GitLab) for issues tagged with a trigger label, then dispatches sandboxed Claude Code Docker containers to implement them. It features an Angular dashboard for monitoring, configuration, and intervention.

## Commands

```bash
# Build
dotnet build

# Test
dotnet test

# Run locally (wires services)
dotnet run --project src/Foundry.AppHost

# Run single test
dotnet test --filter "FullyQualifiedName~ExampleTests.ExampleTest"

# Frontend
cd src/foundry-web
npm install
npx ng serve
```

## Architecture

**.NET 10 / .NET Aspire** monorepo:

| Project | Role |
|---------|------|
| `AppHost` | Aspire orchestrator — service wiring |
| `ServiceDefaults` | OpenTelemetry, health checks, resilience defaults |
| `WebApi` | ASP.NET Core Minimal API backend |
| `Contracts` | Shared DTOs (sealed records) |
| `foundry-web` | Angular 21 SPA (prefix: `fd`) |

### Test Projects

| Project | Role |
|---------|------|
| `WebApi.UnitTests` | Unit tests |
| `WebApi.IntegrationTests` | Integration tests (Testcontainers + SQLite) |
| `Testing` | Shared test infrastructure |

### Vertical Slices

```
src/Modules/
├── Monitoring/
│   ├── Foundry.Modules.Monitoring/          # Repo polling, issue detection
│   └── Foundry.Modules.Monitoring.Contracts/
├── Workers/
│   ├── Foundry.Modules.Workers/             # Container orchestration, lifecycle
│   └── Foundry.Modules.Workers.Contracts/
└── Issues/
    ├── Foundry.Modules.Issues/              # Issue state machine, lifecycle labels
    └── Foundry.Modules.Issues.Contracts/
```

### Key Patterns

- **Result pattern** — handlers return `Result<T>`, endpoints call `result.Match()`
- **Value objects** — strongly-typed IDs and domain concepts
- **Entity factories** — private constructors, static `Create()` methods
- **CQRS** — commands and queries with dedicated handlers
- **SignalR** — real-time worker status and log streaming
- **Transactional outbox** — integration events are persisted with the state change, then delivered at-least-once (see below)

### Database

SQLite via EF Core. Data stored in `data/foundry.db` (configurable via connection string).

### Integration Events & Outbox

Cross-module writes flow through a transactional outbox, so a committed state change and its integration events are one atomic, at-least-once-delivered unit.

- **Enqueue** — code raises integration events via `IIntegrationEventDispatcher`, which only enqueues them into a scoped `IntegrationEventCollector`. Publications never invoke handlers in-process.
- **Harvest in-transaction** — an `OutboxSaveChangesInterceptor` drains the collector into the `outbox_messages` table on each `SaveChanges`, within the same transaction as the state change. Events enqueued before `TransitionAsync` are harvested on the first in-transaction `SaveChanges` (the entity remove/add flushes). Bridge-handler events enqueued during domain-event dispatch are harvested on a trailing `SaveChanges`. All saves share one transaction so the committed state change plus all outbox rows are one atomic unit.
- **Relay** — `OutboxRelayService` (a single, host-level `PeriodicBackgroundService`) polls unpublished rows sequentially ordered by `occurred_at`, delivers each via `IIntegrationEventProcessor`, marks it published, and records failures with bounded retries (poison rows dead-letter via `attempts`/`error`).
- **Inbox dedup** — before invoking a handler the processor checks the `processed_events` table keyed by `(event_id, handler)`, so redelivery is a no-op and handlers are replay-safe.
- **Retention** — the relay prunes published `outbox_messages` older than the retention window on a throttled cadence.
- **Ephemeral broadcasts** — pure SignalR-notification events with no durable consumer (e.g. Docker availability, dispatch pause) are delivered directly via `IIntegrationEventProcessor` rather than the outbox.

### Worker Containers

Dispatched via Docker socket (`IWorkerOrchestrator` abstraction). Workers push to `<prefix>/<issue-number>-<slug>` branches (e.g. `feat/42-add-retry`) and open a PR. Foundry tracks outcomes by polling the container and querying the provider after the container exits.

## Code Style

See `.editorconfig`. Key rules:
- **No `var`** — explicit types enforced
- **Private fields**: `_camelCase`
- **File-scoped namespaces**
- **Braces required** on all blocks
- Centralized package versions in `Directory.Packages.props`
