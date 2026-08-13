# Durable nextProbeAt inside SpendState.Blocked

## Context

Credit-exhaustion blocks are indefinite — a money-based 429 carries no reset time. The clearing moment (Team credit auto-reload, an admin raising the spend cap) is unknowable, so Foundry polls with a probe. The probe schedule must survive a host restart; an in-memory timer would strand a blocked account with no scheduled probe after a crash.

## Decision

`SpendState.Blocked` carries a `DateTimeOffset NextProbeAt`. `ClaudeAccount.BlockSpend(nextProbeAt)` sets it; `RearmProbe(nextProbeAt)` refreshes it without leaving `Blocked`. `CreditProbeService` polls the persisted arm each tick rather than tracking a schedule in memory.

## Consequences

The probe schedule is crash-safe and observable through the normal entity load. The `SpendStateJsonConverter` and the EF column must round-trip the timestamp, and a data migration re-backfills existing `blocked` rows that predate the field. The arm is coupled to persistence, so every schedule change is a save — acceptable at the probe's cadence.
