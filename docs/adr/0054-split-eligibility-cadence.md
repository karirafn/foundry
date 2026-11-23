---
reaches: non-functional
---

# Split Repository-Eligibility Cadence: Branch Rules Per-Cycle, Write Probes Event-Triggered

## Context

Repository eligibility combines two checks of very different cost: a cheap branch-rules GET
and an expensive GitHub write probe (three content-creation POSTs against a 500/hour secondary
rate limit). [ADR 0013](0013-repository-level-eligibility.md) made the whole evaluation an unconditional per-cycle poll step so a fixed
repository heals automatically on the next poll. A later decision stated write probes run "on add
or recheck". The code followed ADR 0013 and issued write probes every cycle, driving approximately
756 POSTs/hour at a typical poll interval and breaching the secondary limit. Branch rulesets and
push permissions change approximately never, so probing every cycle is pure waste.

The "runs every cycle" clause of ADR 0013 and the later "on add or recheck" cadence were
therefore in direct contradiction; only one can be correct. The write probe is a live probe rather than the shared
`permissions.push` flag, so it cannot be made cheap by reading a field instead.

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

Two verdicts also re-probe on their own, so neither is an absorbing state:

- **`Unknown` self-heals on a 15-minute cooldown.** A repository whose stored verdict is `Unknown`
  (a never-probed repository, or a probe that failed or was rate-limited) is re-probed on the next
  poll cycle once `MonitoredRepository.WriteProbeCooldown` has elapsed since its last attempt. A
  failed automatic probe stamps the attempt time (`WriteProbeVerdict.Unknown.LastAttemptedAt`), so
  the next retry is one cooldown away rather than immediate.
- **`Denied` is re-probed after a one-hour cooldown** (`MonitoredRepository.DeniedWriteProbeCooldown`),
  because a permission gained in place on the provider leaves the token unchanged and so raises no
  credential event ([ADR 0115](0115-one-account-token-verified-against-every-permission-foundry-uses.md)).

A verdict taken against an older permission list is re-probed as unprobed. A current `Granted`
stays strictly event-triggered.

This supersedes the "runs every cycle" clause of ADR 0013 for the write-probe half only — the
branch-rules half remains an unconditional per-cycle step as originally decided.
It realises the "on add or recheck" cadence.

## Consequences

- Steady-state polling issues zero write probes for a granted repository; the GitHub secondary rate
  limit is no longer approached during normal operation.
- A write-permission loss is picked up by manual re-check or credential update, not automatically
  on the next poll. An automatic push-failure trigger is deferred to a follow-up (no
  distinguishable push-permission-failure signal exists in the worker exit taxonomy today).
- Auto-heal for branch rulesets is unchanged: a protection-rule change is reflected on the very
  next poll cycle.
- Sizing constraint: while re-probing, a repository issues 3 content-creation POSTs per probe, so
  the worst case is `R × 3 × 60 / C` POSTs/hour for `R` repositories at a `C`-minute cooldown —
  84/hour for 7 repositories at 15 minutes, against a 500/hour GitHub secondary limit, and zero in
  steady state. Past roughly 40 repositories the 15-minute window exceeds the budget; the global
  request governor ([ADR 0066](0066-provider-api-request-budget-governor.md)) inherits this
  constraint rather than a second local throttle.

## Revisions
- 2026-08-25 — `Unknown` self-heals: re-probed on the next poll once a 15-minute cooldown has elapsed since its last attempt (#465).
- 2026-11-18 — A `Denied` verdict is re-probed after a one-hour cooldown, and a verdict taken against an older permission list is re-probed as unprobed ([ADR 0115](0115-one-account-token-verified-against-every-permission-foundry-uses.md)).
