---
reaches: hidden-invariant
---

# 0073. Stable handler dedup identity

## Context

The inbox dedup entry was keyed on `handler.GetType().FullName` in
`IntegrationEventProcessor`. Moving or renaming a handler class silently reset
replay protection for every message still eligible for redelivery to it — no
compiler error, no test failure, no log line. The live database already showed
one handler under two different `FullName` strings after a namespace move that
`rules/vertical-slices.md` itself prescribes for source-named reaction folders.
The redelivery horizon is bounded at `TickInterval (2s) × MaxAttempts (10) = 20s`
(defaults from `OutboxOptions` in `src/Foundry.Shared.Infrastructure/Outbox/OutboxOptions.cs`),
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

`processed_events` rows keyed under the earlier `FullName` scheme were accepted
as expired rather than migrated: the identity vocabulary is not a mechanical
transform of the old strings, and handlers are already replay-safe by the inbox
design. Those rows age out under the retention prune.

## Consequences

- Renaming or moving a handler class no longer resets its replay protection.
- The dedup identity is a second invariant per handler; a missing or duplicate
  one is now a loud boot failure instead of a silent runtime reset.
- The `processed_events.handler` column value vocabulary changes from CLR full
  type names to `<Module>.<HandlerName>` strings — more readable, greppable,
  and decoupled from code structure.
