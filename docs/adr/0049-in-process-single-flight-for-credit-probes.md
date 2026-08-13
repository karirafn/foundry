# In-process single-flight for credit probes

## Context

Scheduled probes (`CreditProbeService`) and the operator "Check now" action (`POST /api/credentials/probe`) can race. Concurrent probes waste containers and could double-publish `CreditsRestored` or double-write `SpendState`.

## Decision

A singleton `CreditProbeCoordinator` holds a `SemaphoreSlim(1,1)`. Both entry points call `TryRunProbeAsync`; a second concurrent caller acquires with a zero timeout, fails fast, and returns an "already running" no-op that reports the in-flight probe. This relies on Foundry being single-instance — the same assumption the host-level `OutboxRelayService` singleton already makes.

## Consequences

The simplest correct mechanism for the current single-instance deployment; no distributed coordination. A future multi-instance deployment would need a distributed lock or leader election for the probe — a documented scaling caveat, not a present concern.
