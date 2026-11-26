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
dotnet test --project tests/Foundry.UnitTests --filter-class "*ExampleTests"

# Frontend — npm ci installs from the lockfile without rewriting it; reach for
# npm install <pkg> only to add or change a dependency. Use the npm version
# pinned in package.json `packageManager` (CI installs it), so lockfile writes match.
cd src/foundry-web
npm ci
npx ng serve

# Frontend layout specs (*.layout.spec.ts) run in headless Chromium, not jsdom.
# Locally run `npx playwright install chromium` once first; inside a worker the
# image's Chromium is used through CHROME_BIN. A layout spec not named
# *.layout.spec.ts runs in jsdom, where every box measures 0.
cd src/foundry-web
npx ng run foundry-web:test-layout --watch=false
```

## Architecture

**.NET 10 / .NET Aspire** monorepo:

| Project | Role |
|---------|------|
| `AppHost` | Aspire orchestrator — service wiring |
| `ServiceDefaults` | OpenTelemetry, health checks, resilience defaults |
| `WebApi` | ASP.NET Core Minimal API backend |
| `Contracts` | Shared DTOs (sealed records) |
| `foundry-web` | Angular 22 SPA (prefix: `fd`) |

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

- **Deterministic first** — code owns every transition, ordering, retry, and side effect; Claude is used only for judgment code cannot express, and returns a schema-validated verdict that code acts on (ADR 0094)
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
- **Relay** — `OutboxRelayService` (a single, host-level `PeriodicBackgroundService`) polls unpublished rows sequentially ordered by `occurred_at`, delivers each via `IIntegrationEventProcessor`, marks it published, and records failures with bounded retries (poison rows dead-letter via `attempts`/`error`). When a handler throws, the relay first clears the scoped DbContext's change tracker and discards events still pending in the `IntegrationEventCollector`, then records the failure — so a failed handler's tracked changes and events are never committed with the failure record. Handlers that commit in several saves must therefore guard against redelivery after a partial run (e.g. `IssueClaimedHandler` skips when its `WorkerRun` already exists).
- **Inbox dedup** — before invoking a handler the processor checks the `processed_events` table keyed by `(event_id, handler)`, so redelivery is a no-op and handlers are replay-safe. The inbox is bounded by the same retention window as the outbox (rather than growing without bound); `ProcessedEvent.ProcessedAt` uses the canonical ISO-8601 `"O"` encoding matching `OutboxMessage`.
- **Retention** — the relay prunes published `outbox_messages` older than the retention window on a throttled cadence. The same sweep also prunes `processed_events` (the inbox) older than the same retention window, deleted in bounded batches (`InboxPruneBatchSize`, default 5000); a full batch re-arms the next tick to sweep again immediately, while an underfull batch restores the throttled cadence.
- **Ephemeral broadcasts** — pure SignalR-notification events with no durable consumer (e.g. Docker availability, dispatch pause) are delivered directly via `IIntegrationEventProcessor` rather than the outbox.

### Worker Containers

Dispatched via Docker socket (`IWorkerOrchestrator` abstraction). Workers push to `<prefix>/<issue-number>-<slug>` branches (e.g. `feat/42-add-retry`) and open a PR. Foundry tracks outcomes by polling the container and querying the provider after the container exits.

Every worker container is hardened (ADR 0088, single source `WorkerHardeningSpec`): `CapDrop: [ALL]` with only `SETUID`/`SETGID` added back for the entrypoint's root-render-then-`setpriv` drop, `no-new-privileges`, the daemon's default seccomp profile, and a read-only root filesystem with a tmpfs at `/etc/claude-code` and anonymous volumes (removed with the container) at `/workspace`, `/home/node`, `/tmp`, `/var/tmp`. Docker-enabled workers layer `RootlessDindRunSpec` on top. Anything a worker needs writable must live under one of those paths.

No worker container spec carries `GIT_PAT` (ADR 0100). Before each run the model-free **Publisher** container (`workers/publisher/`, alpine + git, hardened by `WorkerHardeningSpec.ApplyToPublisher`) fetches the repository with the token and writes `in/base.bundle` to the run's Hand-off volume (`foundry-handoff-<runId>`, mounted read-only at `/handoff` in the worker); the entrypoint clones from that bundle. The Publisher reports the run's base on one `FOUNDRY_BASE` stdout line, persisted on the run.

## Code Style

See `.editorconfig`. Key rules:
- **No `var`** — explicit types enforced
- **Private fields**: `_camelCase`
- **File-scoped namespaces**
- **Braces required** on all blocks
- Centralized package versions in `Directory.Packages.props`

## Documentation

### Decision records

Architectural decisions live in `docs/adr/NNNN-slug.md`. `scripts/check-adr-numbering.sh` enforces the format in CI.

An ADR qualifies only when **both** hold:

1. **The decision reaches beyond one class or feature slice.** It shapes one of:
   - the system's structure
   - a contract between modules
   - a contract with an external system
   - a non-functional property (security, performance, availability, data retention)
   - a dependency choice
   - an invariant that a change elsewhere could break without knowing why
2. **Reversing it needs meaningful rework.**

Front matter records which one it reaches, and holds only this key:

```yaml
---
reaches: structure | module-contract | external-contract | non-functional | dependency | hidden-invariant
---
```

Exactly one value. `status:`, `supersedes:` and `superseded-by:` are not used.

These never get an ADR. Each one's reasoning goes elsewhere instead:

| Decision | Where its reasoning goes |
|---|---|
| A choice inside one class, endpoint, DTO or UI widget | A short why-comment at that code, only when the why is not obvious from the code |
| A migration step, or "X stays until Y" | The issue or PR |
| Applying an existing ADR or convention | A citation of that ADR or convention |
| The fix itself | The commit message. A lasting rule a fix establishes is judged by the gate. |
| Design not built yet | An issue |

One ADR records one decision.

**Amend in place.** A changed decision rewrites Context or Decision to state current truth and adds one line to a trailing `## Revisions` list:

```markdown
## Revisions
- 2026-11-20 — <what changed> (<ADR NNNN or #issue>)
```

A wholly replaced ADR is deleted, and every citation of it is re-pointed to the successor. ADR numbers are never reused or renumbered, and gaps stay.

When implementing an issue, add a file under `docs/adr/` only when the issue carries a `## Decision record` section; commit it with the code, changing it only where the implementation forced a change, and say so in the PR description. Put every other decision's reasoning in a code comment, the PR, or the commit message.
