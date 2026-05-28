# FailureReason Stored as JSON Column

## Context

`FailedRun` needs to record why a worker container failed.
Options were: (a) separate discriminator column per failure variant, (b) a sealed record hierarchy serialised to a single JSON `TEXT` column.

## Decision

Store `FailureReason` as a JSON-serialised `TEXT` column on the `worker_runs` table, using a `ValueConverter<FailureReason, string>` with `[JsonDerivedType]` polymorphic deserialisation.
The failure reason is always loaded and stored as a whole unit, never queried by variant in SQL, and the type hierarchy can be extended with new subtypes without requiring a schema migration.

## Consequences

New `FailureReason` variants require only a `[JsonDerivedType]` attribute — no migration needed.
Individual failure-reason properties (e.g. exit code) cannot be filtered in SQL without loading and deserialising the column.
This is acceptable because the only consumer is the domain aggregate, which always loads the full entity.
