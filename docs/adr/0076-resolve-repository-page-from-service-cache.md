# Resolve the Repository Page From the Service Cache, Not a By-Id Fetch

## Context

The routed repository page (#536) needs the repository identified by its URL `:repositoryId`. Foundry exposes no single-repository GET endpoint — only the per-account list `GET /api/accounts/{accountId}/repositories` (`RepositoryEndpoints.cs`) — and #536 forbids backend changes.

## Decision

The page resolves its repository as a `computed` lookup by id over `RepositoryService.repositories()`, the collection the list already loads, rather than fetching the repository on its own. On cold load or deep link the page triggers the existing `loadAllRepositories(accountIds)` and derives a loading / load-error / not-found / loaded view-state from the service signals, gating the not-found verdict on the load having settled. The page keeps no copy of the repository, so there is a single source of truth and a delete performed anywhere flips an open page to not-found reactively.

## Consequences

- No new endpoint or contract; the page is pure routing and layout over the existing list read, matching the #536 scope.
- The page is reactive to updates, rechecks, and deletes made elsewhere for free.
- A deep link cannot fetch one repository in isolation — it loads every monitored repository first. Acceptable at current scale (settings list); if repository counts grow large, add a `GET /api/repositories/{id}` and switch the page to a resolver, which would supersede this ADR.
- The not-found verdict depends on the settle-gate; without it a deep link would flash not-found before the list arrives (covered by test).
