# 0073. Stable handler dedup identity

## Context

The inbox dedup entry was keyed on `handler.GetType().FullName` in
`IntegrationEventProcessor`. Moving or renaming a handler class silently reset
replay protection for every message still eligible for redelivery to it — no
compiler error, no test failure, no log line. The live database already showed
one handler under two different `FullName` strings after a namespace move that
`rules/vertical-slices.md` itself prescribes for source-named reaction folders.
The redelivery horizon is bounded at `TickInterval (2s) × MaxAttempts (10) = 20s`,
so the blast radius is small, but the failure is silent — that is the defect.

## Decision

Each integration-event handler declares a stable, explicit dedup identity via
`[IntegrationEventHandlerIdentity("<Module>.<HandlerName>")]`. The
registration helper `AddIntegrationEventHandler<TEvent, THandler>` records the
identity into a singleton `HandlerDedupIdentityRegistry` at boot. The processor
resolves the dedup key from the registry instead of `Type.FullName`. A missing
attribute throws at registration; a duplicate identity throws at startup
(`Validate()` in the composition root) — the wrong state is unrepresentable at
boot. The identity string is chosen once and frozen; a later class rename or
namespace move does not change it.

Existing `processed_events` rows keyed under the old `FullName` scheme are
explicitly accepted as expired rather than migrated. The new identity vocabulary
is not a mechanical transform of the old strings, so a SQL rewrite would embed 32
hand-mapped literals and require a backfill entry for every future handler. At
the single deploy, a message still within its ≤20s redelivery horizon may invoke
its handler once more, because the old-scheme row does not match the new-scheme
lookup. Handlers are already required to be replay-safe by the inbox design, so a
one-off duplicate invocation is tolerable. Old rows age out under the existing
retention prune.

## Consequences

- Renaming or moving a handler class no longer resets its replay protection.
- The dedup identity is a second invariant per handler; a missing or duplicate
  one is now a loud boot failure instead of a silent runtime reset.
- The `processed_events.handler` column value vocabulary changes from CLR full
  type names to `<Module>.<HandlerName>` strings — more readable, greppable,
  and decoupled from code structure.
- A bounded (~20s) one-time window of at-most-once-extra invocation exists at the
  deploy that introduces this change; no migration code is required.
