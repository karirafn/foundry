# Canonical ISO-8601 encoding for inbox timestamps

## Context

`processed_events.processed_at` was persisted via EF Core's default `DateTimeOffset`-to-string
conversion on SQLite, producing a variable-width, space-separated, offset-suffixed encoding
(e.g. `2026-07-26 13:05:41.098579+00:00`) with trailing zeros trimmed. `outbox_messages`
already used a canonical `"O"` (round-trip) encoding. The retention sweep needs to prune
`processed_events` by age with `Where(p => p.ProcessedAt < cutoff)`, but EF Core 10's SQLite
provider cannot translate a comparison on an unconverted `DateTimeOffset` — the query throws
`InvalidOperationException`. A range prune also requires that stored strings sort chronologically,
which the trimmed-fractional legacy encoding does not guarantee within a single second.

## Decision

Encode `ProcessedEvent.ProcessedAt` with the same canonical ISO-8601 `"O"` value converter used
by `OutboxMessage` (`dto => dto.UtcDateTime.ToString("O")`), stored as TEXT. This makes the
column fixed-width (28 chars, always 7 fractional digits, trailing `Z`), lexicographically
sortable in chronological order, and translatable for the range prune on SQLite. A one-off
migration rewrites in-window legacy-encoded rows to the canonical form (padding the fractional
part to 7 digits); out-of-window legacy rows sort below every cutoff and are removed by the
age sweep without rewriting.

## Consequences

- The inbox prune query translates and runs on SQLite; canonical strings sort correctly, so a
  range delete is safe.
- Inbox and outbox timestamps now share one encoding, removing a cross-table inconsistency.
- The rewrite is not cleanly reversible (SQLite's trailing-zero trimming cannot be reconstructed,
  and post-migration canonical rows are indistinguishable from rewritten ones), so `Down()` is a
  no-op; the canonical form is forward-compatible.
- The rewrite migration runs in Development only (matching the existing `Database.Migrate()`
  policy — `src/Foundry.WebApi/Program.cs:151`, guarded by `IsDevelopment()`), so production
  legacy rows drain via the age sweep rather than being rewritten — safe because no code
  compares `processed_at` for equality (dedup keys on `(EventId, Handler)`).
