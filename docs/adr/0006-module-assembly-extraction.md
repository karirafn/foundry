# Module Assembly Extraction

## Context

The WebApi project contained all three domain modules (Monitoring, Issues, Workers) as folders, making it impossible to enforce `internal` visibility at the module boundary. Any handler, entity, or service could be accidentally referenced by other modules or the composition root.

## Decision

Each module was extracted to its own assembly (`Foundry.Modules.<Name>`), with a companion Contracts project (`Foundry.Modules.<Name>.Contracts`) as the narrow public surface. All module types default to `internal`; only types consumed cross-module are `public` in Contracts.

Cross-module writes use integration events (outbox pattern) rather than direct method calls. Cross-module reads use query interfaces published in Contracts. The `IIssuesModule` god-interface was replaced by `IIssueQueries` (reads) and `WorkerCapacityAvailable` → `IssueClaimed` event choreography (writes).

`FoundryDbContext` removes all `DbSet<T>` properties; modules access data via `Set<T>()`. Entity type configurations are discovered via `ApplyConfigurationsFromAssembly` using a sentinel type from each module assembly.

## Considered Options

- **Folder-only modules inside WebApi** — rejected because the compiler does not enforce `internal` across folders in the same assembly.
- **Shared DbContext with DbSet properties** — rejected because it created a god-context that grew with every module and exposed module internals through the host.

## Consequences

- Accidental cross-module dependencies are caught at compile time.
- Adding a new module requires a new csproj pair and an `ApplyConfigurationsFromAssembly` call in `FoundryDbContext`.
- EF migrations live in WebApi and reference each module assembly by sentinel type, so new entity configurations are auto-discovered without changing `FoundryDbContext`.
