# Typed Worker Provider

> **The `RepositoryDispatchInfo`-carries-a-string clause of this ADR is superseded by [ADR 0051](0051-dispatch-context-union-and-typed-dispatch-seam.md).** `RepositoryDispatchInfo` now carries `WorkerProvider Provider` directly; `WorkerProvider.FromDiscriminator` is deleted. The core decision — closed record hierarchy over enum for provider identity — still stands.

## Context

Workers need to behave differently depending on whether the monitored repository is hosted on GitHub or GitLab — for example, configuring the appropriate CLI auth helper. The dispatch pipeline must carry provider identity from the account to the worker container.

## Decision

Represent the provider as a closed record hierarchy (`WorkerProvider` with `GitHub` and `GitLab` variants) rather than an enum or runtime host-sniffing.

`RepositoryDispatchInfo` carries the raw discriminator string (`ProviderType`) projected from the EF TPH `"type"` shadow property. Callers convert to `WorkerProvider` via `WorkerProvider.FromDiscriminator` at the point of use. EF cannot instantiate abstract records inside `Select`, so the string is carried across the boundary and mapped on the application side.

## Considered Options

- **Enum** — simpler to define, but produces anemic models. Switch statements scatter provider logic across services; adding a third provider means hunting all switch sites.
- **Runtime host-sniff** — infer provider from `CloneUrl` hostname. Brittle: self-hosted GitHub Enterprise and GitLab share no distinguishing hostname pattern, and a GHES instance at `git.internal` is indistinguishable from any other self-hosted SCM.
- **Closed record hierarchy (chosen)** — exhaustive pattern matching is enforced by the compiler (with `UnreachableException` for the discard arm). Provider-specific behaviour is co-located with the variant. Adding a third provider (e.g., Gitea) is a single new nested record plus a factory arm.
