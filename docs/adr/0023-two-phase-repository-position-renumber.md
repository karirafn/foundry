# Two-Phase Transaction for Repository Position Renumber

## Context

`MonitoredRepository.Position` is a 0-based, contiguous, unique integer that governs dispatch order.
Reordering (move up/down) and deletion must renumber the affected rows to keep the sequence contiguous.
SQLite enforces the `UNIQUE` constraint per-row at flush time with no support for deferrable constraints,
so a naive sequential renumber collides mid-flush when two rows temporarily hold the same position.

## Decision

Renumber using a two-phase strategy inside a single EF Core transaction.

Phase 1 — shift affected rows into a collision-free offset band (current value + 1,000,000) and call `SaveChangesAsync`.
Phase 2 — assign final contiguous 0..n-1 values and call `SaveChangesAsync` again.
Both saves share one `IDbContextTransaction` so the intermediate state is never visible outside the transaction.

## Considered Options

- **Drop the unique index and rely on the single-server write path** — rejected: loses the DB-level guarantee; a bug or future concurrent write could produce duplicate positions that are hard to detect and repair.
- **Deferrable unique constraint** — rejected: SQLite does not support `DEFERRABLE INITIALLY DEFERRED` on unique indexes.

## Consequences

Concurrent moves are last-write-wins — the second transaction overwrites the first.
This is acceptable for a single-operator phase-1 POC where simultaneous drag-and-drop reorders are not a realistic scenario.
