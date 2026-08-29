# Wrap the Available-Repositories Response to Disambiguate an Empty Picker

## Context

The add-repository picker is filtered to the selected account's claimed namespaces (issue #330). A bare `AvailableRepository[]` response cannot tell the operator *why* the list is empty: an account with no Namespace Claims, an account whose claims cover no visible repos, and a dead/failed token all render as the same empty list. Load failure already surfaces via the HTTP error path; the remaining ambiguity is "no claims" versus "claims but nothing under them".

## Decision

The endpoint returns a wrapper, `AvailableRepositoriesResponse(bool HasClaims, IReadOnlyList<AvailableRepository> Repositories)`, where `HasClaims = credential.Namespaces.Count > 0`. The frontend renders three distinct terminal states: `!hasClaims` → "no claimed namespaces", `hasClaims && empty` → "no repositories under this account's claimed namespaces", and the existing load-error box. The frontend `RepositoryService` exposes a `availableHasClaims` signal alongside the repository list.

## Considered Options

- **Bare list** — rejected: an empty array is indistinguishable from a dead token or a claimless account, the exact ambiguity this issue set out to remove.
- **A separate `/claims` endpoint queried alongside** — rejected: adds a round trip and a second source of truth for a fact the listing already knows.

## Consequences

- A response-contract change: the handler result type moves from `IReadOnlyList<AvailableRepository>` to the wrapper, rippling through the `Query` type, DI registration, integration stubs, the frontend service, and both picker components in lockstep. Accepted — the project is in development with no external consumers.
- The picker can render an honest, actionable empty state instead of a silent blank list.
