# Provider API Request Budget Governor

## Context

Nothing in Foundry owned the provider API request budget, so every new poll pass could
silently multiply it with no test to catch the regression.
Documentation alone had already failed here: [ADR 0041](0041-probe-target-selection.md) stated
that eligibility write probes run "on add or recheck", yet the code ran them every cycle because
[ADR 0013](0013-repository-level-eligibility.md) established an unconditional per-cycle step and
nothing reconciled the two ADRs — the contradiction was left standing until
[ADR 0054](0054-split-eligibility-cadence.md) resolved it (see Finding below).

This ADR records three decisions made together as part of issue #454:

1. Foundry reads issues via **GitHub GraphQL** and consumes the `rateLimit` envelope it already
   parses, making provider headroom observable.
2. The per-cycle provider call cost is bounded by a named constant and enforced by a compile-time
   invariance test.
3. A governor records and surfaces headroom but **does not block polling**.

## Provider Rate Budget Table

| Budget | Limit |
|---|---|
| GitHub REST, authenticated user | 5,000 req/hour |
| GitHub GraphQL, authenticated user | 5,000 points/hour (separate budget; cost = requests/100, min 1) |
| GitHub content creation (secondary) | 80/min and 500/hour |
| GitHub per-endpoint points (secondary) | 900/min REST, 2,000/min GraphQL; GET = 1, POST/PATCH/PUT/DELETE = 5 |
| GitHub concurrency (secondary) | 100 in flight |
| gitlab.com authenticated API | 2,000 req/min |
| Conditional GET returning 304 | does not count against the GitHub primary limit |

GitHub REST and GitHub GraphQL are **separate budgets** — a floor breach on one must not gate the
other.
GitLab reports limits differently and sits nowhere near its ceiling under typical Foundry load;
GitLab headroom is recorded for visibility but is not evaluated against a floor.

## Decision

### 1. GraphQL for issue reads

Foundry reads monitored-repository issues via the GitHub GraphQL API.
The GraphQL response envelope already contains `rateLimit { cost remaining limit resetAt }`.
`remaining` is recorded by the governor on every GraphQL response; `limit` and `resetAt` are
carried for display.
The GraphQL migration itself is a separate issue; this decision records the intent and establishes
the headroom-recording path.

### 2. Named fixed-cost cap constant

The fixed per-cycle provider call cost — the count of API calls a poll cycle issues that does
**not** scale with issue count — is bounded by `RepositoryPoller.MaxFixedPollCallsPerCycle`.
ADR 0066 and DOMAIN.md point at this constant by name rather than restating its numeric value,
so there is exactly one place to change and the change is reviewed at the constant.

The invariance test asserts that driving `RepositoryPoller.PollAsync` with 5 issues and with 200
issues produces an identical total provider-call count, and that the fixed-cost scenario (no
review issues, no dependency candidates) remains within `MaxFixedPollCallsPerCycle`.
A new unconditional call that breaks either assertion fails the build.

### 3. Governor: records + surfaces + reports; fails open; does not block

When headroom falls below a configured floor the governor records and surfaces the condition
(`ProviderBudgetHealth = Low`) and logs once on the transition.
Polling continues regardless of the verdict.
Absence of a headroom reading produces `ProviderBudgetHealth = Unknown`, never `Low` — missing
data is never treated as exhaustion.

**Justification.**

**Absorbing-state rule** (see `rules/coding.md`): "Give every degraded, failed, or unverified
state an automatic exit … a guard whose only escape is the action it suppresses … absorbs."
A hard block on polling is exactly this trap: provider headroom only refreshes when Foundry makes
a request, and the only requests Foundry makes against that budget are the poll calls the block
would suppress.
A blocked governor therefore has no automatic liveness path — its sole exit is the very action it
forbids.
Reporting has no such trap: polling continues, headroom keeps refreshing, and the verdict
self-clears the moment `remaining` climbs back above the floor.

**Fail open on unreadable signals** (the risk named in issue #454): blocking makes a parse bug or
a misconfigured floor a total monitoring outage; reporting makes the same bug a stale dashboard
number.

**`x-ratelimit-reset` is a timestamp, not a duration.** A blocking design would have to gate on
"is the window still exhausted", which depends on comparing a persisted reset timestamp against
`now` — precisely the staleness trap the edge cases warn about.
Reporting sidesteps it: the store holds the last observed `remaining` plus `ObservedAt`, and a
stale reading degrades to `Unknown` rather than masquerading as current headroom.

**The real per-cycle protection is the invariance test and the cap constant**, not a runtime
brake.
The governor's job is observability and early warning; enforcement is the compile-time invariance
test.

**Staleness rule.** A reading whose `ObservedAt` age exceeds the staleness window produces
`Unknown`, never `Low`.
The store is in-memory (no cross-restart persistence), so the window only matters within a single
process lifetime.

## Finding: ADR 0013 vs ADR 0041 contradiction already resolved

The contradiction that issue #454 asked to resolve is already settled in the repository:

- [ADR 0054](0054-split-eligibility-cadence.md) is the resolution: branch-rules GET stays
  per-cycle (ADR 0013's auto-heal guarantee), write probes become event-triggered (ADR 0041's "on
  add or recheck" cadence), with `Unknown` self-healing on a 15-minute cooldown. ADR 0054
  explicitly states it "supersedes the 'runs every cycle' clause of ADR 0013 for the write-probe
  half only".
- ADR 0013 carries a "Partially superseded" banner linking ADR 0054, scoping the superseded
  clause to the write-probe half. It must not be re-amended here.
- ADR 0054's 2026-08-25 amendment names issue #454 as the owner of the global request governor
  ("a global request governor (#454) inherits this constraint rather than a second local
  throttle"). This ADR is the back-reference that closes that loop.

This ADR therefore does **not** re-edit ADR 0013 or re-state the cadence resolution.
AC7's requirement ("the ADR 0013 vs ADR 0041 contradiction … is resolved rather than left
standing") is satisfied by construction — by ADR 0054, not by this ADR.

## Considered Options

- **Governor blocks polling on floor breach** — rejected; creates an absorbing state with no
  automatic exit (headroom only refreshes by making the requests the block suppresses). See
  justification above.
- **Cadence derived from live headroom** (poll faster when remaining is high) — rejected: makes
  pickup latency nondeterministic and hard to test; deferred to a follow-up.
- **Operator-editable floor** — deferred; AC5 requires the floor be operator-*visible*, not
  operator-*editable*. The floor ships as a named constant (`ProviderBudgetPolicy.DefaultFloor`)
  surfaced read-only on the dashboard; an editable floor is a clean follow-up.

## Consequences

- The governor records `ProviderBudgetHealth` per budget key (`GitHubRest`, `GitHubGraphQl`,
  `GitLabRest`); the verdict for each key is independent.
- `GitLabRest` headroom is recorded for visibility; no floor is evaluated against it.
- A `Low` verdict is logged once on transition and surfaced on the dashboard; a stale or absent
  reading surfaces as `Unknown`.
- The invariance test and `RepositoryPoller.MaxFixedPollCallsPerCycle` form the compile-time
  enforcement layer. Any new unconditional provider call must update the constant, and the test
  catches it.
- The write-probe sizing constraint from [ADR 0054](0054-split-eligibility-cadence.md)
  (approximately 40-repository ceiling at the 15-minute cooldown against the 500/hour secondary
  limit) is inherited by this governor rather than re-stated with a second local throttle.
- This ADR builds on the rate-limit signal infrastructure established in
  [ADR 0046](0046-parseable-reset-time-as-usage-limit-discriminator.md) (parseable reset time as
  usage-limit discriminator) and [ADR 0058](0058-429-primary-usage-limit-signal.md) (429 as
  primary usage-limit signal).
