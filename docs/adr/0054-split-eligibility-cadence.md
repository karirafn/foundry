# Split Repository-Eligibility Cadence: Branch Rules Per-Cycle, Write Probes Event-Triggered

## Context

Repository eligibility combines two checks of very different cost: a cheap branch-rules GET
and an expensive GitHub write probe (three content-creation POSTs against a 500/hour secondary
rate limit). [ADR 0013](0013-repository-level-eligibility.md) made the whole evaluation an unconditional per-cycle poll step so a fixed
repository heals automatically on the next poll. [ADR 0041](0041-probe-target-selection.md) later stated write probes run "on add
or recheck". The code followed ADR 0013 and issued write probes every cycle, driving approximately
756 POSTs/hour at a typical poll interval and breaching the secondary limit. Branch rulesets and
push permissions change approximately never, so probing every cycle is pure waste.

The "runs every cycle" clause of ADR 0013 and the "on add or recheck" cadence of ADR 0041 were
therefore in direct contradiction; only one can be correct. Cross-reference [ADR 0039](0039-github-write-probe-not-shared-canpush.md) (rejected
the shared `permissions.push` alternative in favour of a live write probe).

## Decision

Split evaluation by call cost.

The **branch-rules GET** stays an unconditional per-cycle poll step.
This preserves ADR 0013's auto-heal guarantee: a configuration change on the provider is reflected
on the next poll without any user action.

The **write probe** becomes event-triggered — it runs on repository add, manual re-check, and
credential update/rotation.
The last write-probe result is persisted on `MonitoredRepository` as a `WriteProbeVerdict` value
object (`Granted` / `Denied` / `Unknown`).
`WriteProbeVerdict` is composed with the fresh branch-rules result each cycle to produce the
current `RepositoryEligibility`.
`Unknown` maps to `Unreachable` so a repository that has never been probed is never dispatchable.

This supersedes the "runs every cycle" clause of ADR 0013 for the write-probe half only — the
branch-rules half remains an unconditional per-cycle step as originally decided.
It realises the cadence stated in ADR 0041.

## Consequences

- Steady-state polling issues zero write probes; the GitHub secondary rate limit is no longer
  approached during normal operation.
- A write-permission change is picked up by manual re-check or credential update, not automatically
  on the next poll. An automatic push-failure trigger is deferred to a follow-up (no
  distinguishable push-permission-failure signal exists in the worker exit taxonomy today).
- Auto-heal for branch rulesets is unchanged: a protection-rule change is reflected on the very
  next poll cycle.
- Requires a migration adding a nullable `write_probe_verdict TEXT` column (`NULL` → `Unknown`).

## Amendment (2026-08-25): Unknown self-heal cooldown

`Unknown` was an absorbing state — the only exits were operator/credential events, so a
migration-backfilled `NULL` verdict or a rate-limited probe parked a repository at `Unreachable`
indefinitely, silently halting dispatch (#465).

`Unknown` now self-heals: a repository whose stored verdict is `Unknown` is re-probed on the next
poll cycle once a **15-minute cooldown** has elapsed since its last attempt. A failed automatic
probe stamps the attempt time (`WriteProbeVerdict.Unknown.LastAttemptedAt`), so the next retry is
one cooldown away rather than immediate. `Granted` and `Denied` remain strictly event-triggered —
`Denied` is knowledge and self-healing it would reintroduce a permanent probe floor for
read-only repositories (#463 owns the push-failure trigger).

Sizing constraint: while failing, a repository issues 3 content-creation POSTs per probe, so worst
case is `R × 3 × 60 / C` POSTs/hour for `R` repositories at a `C`-minute cooldown — 84/hour for 7
repositories at 15 minutes, against a 500/hour GitHub secondary limit, and zero in steady state.
Past roughly 40 repositories the 15-minute window exceeds the budget; a global request governor
(#454) inherits this constraint rather than a second local throttle.
