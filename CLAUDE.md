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

### Vertical Slices (WebApi)

```
WebApi/
├── Modules/
│   ├── Monitoring/         # Repo polling, issue detection
│   ├── Workers/            # Container orchestration, lifecycle
│   └── Issues/             # Issue state machine, lifecycle labels
├── Shared/Abstractions/    # Result, Error, AggregateRoot
├── Infrastructure/         # Cross-cutting (EF, Docker API client)
└── Program.cs
```

### Key Patterns

- **Result pattern** — handlers return `Result<T>`, endpoints call `result.Match()`
- **Value objects** — strongly-typed IDs and domain concepts
- **Entity factories** — private constructors, static `Create()` methods
- **CQRS** — commands and queries with dedicated handlers
- **SignalR** — real-time worker status and log streaming

### Database

SQLite via EF Core. Data stored in `data/foundry.db` (configurable via connection string).

### Worker Containers

Dispatched via Docker socket (`IWorkerOrchestrator` abstraction). Workers push to `<prefix>/<issue-number>-<slug>` branches (e.g. `feat/42-add-retry`) and open a PR. Foundry tracks outcomes by polling the container and querying the provider after the container exits.

## Code Style

See `.editorconfig`. Key rules:
- **No `var`** — explicit types enforced
- **Private fields**: `_camelCase`
- **File-scoped namespaces**
- **Braces required** on all blocks
- Centralized package versions in `Directory.Packages.props`
