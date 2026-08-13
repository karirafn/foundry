# Remove Immediate Requeue, Always Pause on Usage Limit

## Context

When a usage limit was detected with a reset time in the past, `ResolveFailureReasonAsync` returned `isUsageLimitedRequeue: true`, and `WorkerRunFailedHandler` immediately called `.Retry()` to requeue the issue.
This sent issues straight back into an active limit window because the reset time was often stale or inaccurate.

## Decision

A detected usage limit always sets the global `UsageLimitResetsAt` pause and returns `FailureReason.UsageLimited`.
The `WorkerRunFailed.IsUsageLimitedRequeue` parameter and the two `.Retry()` branches in `WorkerRunFailedHandler` are removed.
Recovery is solely via auto-resume (`DispatchResumedHandler`) or manual resume, both of which already retry usage-limited issues.

`GlobalSettings.SetUsageLimitResetsAt` is extend-only and clamps to 7 days, so an unparseable or fallback reset time only ever extends an existing pause — it self-corrects on the next failure.

> **Superseded in part by ADR 0046.** The `DefaultCooldownMinutes` fabricated-fallback path referenced here is removed: a 429 with no parseable reset time is now classified as `CreditsExhausted` (an indefinite spend block), not a fabricated timed pause. The always-pause, no-immediate-requeue decision above stands.

## Considered Options

- **Keep the immediate-requeue branch for past-reset times** (status quo) — rejected: this was the direct cause of issues being stranded into active limits.
- **Add a separate auto-continue setting** — rejected: no scenario where you would resume dispatch but strand limited issues; the existing `AutoResumeOnUsageReset` already covers it.

## Consequences

- Contract change: `WorkerRunFailed.IsUsageLimitedRequeue` removed (acceptable at phase-1 POC maturity).
- Every usage limit pauses dispatch globally; no fast-path retry exists.
