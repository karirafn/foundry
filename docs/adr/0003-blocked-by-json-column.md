# BlockedBy Stored as JSON Column

## Context

Issue dependencies are a small collection of issue numbers (typically 1-3) stored on the Issue aggregate.
Options were: (a) owned entity collection with a shadow join table, (b) JSON-serialized column on the issues table.

## Decision

Store `BlockedBy` as a JSON-serialized `TEXT` column on the `issues` table, following the same pattern used for `Labels`.
The collection is always loaded and stored as a whole unit, never queried independently, and is small enough that JSON serialization overhead is negligible.

## Consequences

Consistent with the existing `Labels` pattern.
No additional table or join overhead.
Individual blocker entries cannot be queried with SQL — the full set must be loaded.
This is acceptable because the only consumer is the aggregate, which always loads the full entity.
