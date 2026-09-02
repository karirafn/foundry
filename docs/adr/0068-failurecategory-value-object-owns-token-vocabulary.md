# FailureCategory Value Object Owns the Failure-Token Vocabulary in Workers.Contracts

## Context

The failure-category token vocabulary had two independent producers — `FailureReason.CategoryToken` in the
Workers module, and a `pr_closed` literal in the Issues module — with no single owning type.
`FailureReason`'s tokens double as compile-time `[JsonDerivedType]` discriminators, and the dependency
graph allows a shared type only in `Workers.Contracts` (which cannot reference the higher-level Workers
module, only the reverse direction is legal).

## Decision

Introduce a `FailureCategory` value object in `Foundry.Modules.Workers.Contracts` owning the ten tokens as
`public const string` fields, a validating factory (`Create` returning `Result<FailureCategory>`), and named
static members (e.g. `FailureCategory.TransientApiError`, `FailureCategory.PrClosed`).
`FailureReason`'s nine discriminator consts each derive from the matching `FailureCategory` const — a
cross-assembly `const` initialisation, which keeps discriminators compile-time.
The three failed-issue entity properties (`FailedIssue`, `ContinuableFailedIssue`, `RevisionFailedIssue`)
become typed `FailureCategory`, persisted via an EF `ValueConverter` to the unchanged `TEXT` column.
The `WorkerRunFailed` contract `Category` stays `string?`; the domain boundary (`WorkerRunFailedHandler`)
converts and falls back to `FailureCategory.NonZeroExit` with a logged warning on an unknown or null token.

## Considered Options

- **Value object in `Foundry.Shared`** — rejected: the vocabulary is a Workers-domain concept;
  `Workers.Contracts` is already referenced by exactly the right set of consumers and carries `WorkerRunId`.
- **`FailureReason` (Workers module) owns the vocabulary; Contracts value object references it** —
  impossible: violates the dependency direction (Contracts cannot reference the Workers module).
- **Change contract `Category` to the value object** — rejected: fights the "no wire-shape change" scope
  and pushes rejection onto deserialization rather than the domain boundary the acceptance criteria name.
- **Keep entity property `string`, validate in the setter path** — rejected: leaves invalid states
  representable on the entity and forces the SQL predicate to compare raw literals.

## Consequences

The vocabulary has a single owner; invalid categories are unrepresentable on entities; the SQL predicate
stays translatable via the converter-vs-constant pattern.
Storage and outbox wire shape are unchanged — no migration, back-compatible rows.
Cost: `FailureReason` and `FailureCategory` share token strings by `const` reference — a token rename must
touch the Contracts owner, and the `[JsonDerivedType]` constraint permanently forbids making the tokens
runtime values.
