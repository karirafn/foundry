# Removed Repositories Are Soft-Deleted and Restored on Re-Add

## Context

Removing a Monitored Repository hard-deleted its row and told no other module, so its issues stayed on the board, and deleting an Account removed only its Namespace Claims, leaving every repository it covered unreachable and its issues stuck.
Completed issues and worker runs are the record of what Foundry did for a repository, and per-repository statistics (completed issues, run outcomes, spend) need that record to stay attached to a stable repository identity across a removal and a later re-add.
No entity in the codebase used soft deletion before this decision.

## Decision

Removal is a lifecycle state on the `MonitoredRepository` aggregate, not a row deletion.

- **State on the aggregate** — `RemovedAt`, set by `Remove(now)` and cleared by `Restore()`; `Remove` clears the repository-owned rows (`IssueWatch`, `PendingMerge`, `PendingRebase`, `PostedComment`) that a hard delete removed by cascade, and takes the repository out of the position order.
- **One named query filter** — EF Core's named global query filter keeps removed repositories out of every LINQ query; only lookups that must see a removed repository bypass it by name: the History slug lookup, and the re-add lookup below.
- **Restore on re-add** — adding a repository whose `(Host, Slug)` matches a removed row restores that row: the add request supplies every operator setting, `Id`, `WebhookHookId` and the webhook secret are kept, and operational state (eligibility, webhook status, `LastPolledAt`, position) starts fresh.
- **What survives removal** — completed issues and all worker runs stay attached to the removed repository's `Id`; every non-completed issue is deleted, and unpublished runs are discarded.
  Open issues return from the provider on the first poll after a re-add, because the provider is the authority on open labelled issues.
- **No retention purge** — a removed repository blocks nothing and its exit is a re-add, so it is kept indefinitely.

## Considered Options

- **Hard delete** — open issues come back from the provider after a re-add, but completed history, runs and repository settings are lost, and no stable identity remains for per-repository statistics.
- **Hard delete with a separate history record** — keeps history, but splits one repository identity across two tables that can drift apart.
- **A generic `IsDeleted` convention with an interceptor** — a shared abstraction with a single consumer, and an interceptor that silently turns `Remove()` into an update hides what removal does from the reader.

## Consequences

- Every query on `MonitoredRepository` inherits the filter; code that must see removed repositories names the filter it bypasses.
- The `(Host, Slug)` unique index still covers removed rows, so a re-add must restore rather than insert.
- Removing a repository and claiming one of its issues each check and write inside one transaction, relying on SQLite's serializable isolation, so a claim can never start a run on a removed repository.
