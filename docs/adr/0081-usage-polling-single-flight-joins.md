# Usage polling single-flight joins rather than fails fast

## Context

Usage polling has two concurrent sources — a 5-minute periodic tick and event-triggered
polls (credentials validated/invalidated, auth-mode change). The credit-probe single-flight
([ADR 0049](0049-in-process-single-flight-for-credit-probes.md)) returns "already running"
to a second caller so the operator's manual probe shows in-flight. Reusing that fail-fast
semantics for usage would silently drop an event-triggered poll that arrives during a tick —
exactly the refresh the event should guarantee.

## Decision

`UsagePollCoordinator` tracks an in-flight `Task<UsageSnapshot?>` under a `Lock`. A second
caller arriving while a poll is in flight joins it — awaiting the same task and returning the
snapshot it produces — rather than starting a second container. Two usage containers never run
concurrently; a joiner adds no container and observes the running poll's result.

## Consequences

At most one usage container at a time; joiners coalesce. The manual-refresh endpoint (#612)
reuses the same coordinator and naturally coalesces with an in-flight tick. Relies on the
single-instance assumption already documented in ADR 0049.
