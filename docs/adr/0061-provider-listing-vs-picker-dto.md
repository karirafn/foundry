# Separate the Provider Listing Record from the Picker Response DTO

## Context

The add-repository picker's `AvailableRepository` record served two roles at once: the raw return type of the HTTP clients' `ListRepositoriesAsync` (feeding `NamespaceDeriver` → `NamespaceDerivation.FromWritableRepositories`), and the shape returned to the picker. Issue #330 adds an `IsMonitored` flag that is meaningful only to the picker — namespace derivation cares only about `Slug` + `CanPush`.

## Decision

Split the one record into two, each named for the stage that produces it:

- `ProviderRepository(Slug, IsPrivate, CanPush)` lives in the infrastructure layer and is what the GitHub/GitLab clients emit — "what the provider listing told us". `NamespaceDeriver`/`NamespaceDerivation` consume this.
- `AvailableRepository(Slug, IsPrivate, CanPush, IsMonitored)` lives in the Repositories feature area and is built by `GetAvailableRepositories.Handler` after filtering to claimed namespaces and stamping the monitored flag.

## Considered Options

- **Add `IsMonitored` to the single shared record** — rejected: leaks a query concern into infrastructure and forces `NamespaceDeriver` to fabricate a flag it never computes, an invalid state that reads as meaningful.
- **Keep one record, leave `IsMonitored` defaulted during derivation** — rejected: the same invalid-state-representable smell; a caller holding a derivation-produced record could read a `false` that means "not computed", not "not monitored".

## Consequences

- The type a caller holds now tells them which stage produced it; no consumer can read `IsMonitored` off a record that never computed it.
- The handler maps `ProviderRepository` → `AvailableRepository`, a small amount of extra mapping in exchange for the clean separation.
