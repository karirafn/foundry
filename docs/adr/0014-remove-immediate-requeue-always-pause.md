---
reaches: external-contract
---

# Remove Immediate Requeue, Always Pause on Usage Limit

## Context

When a usage limit was detected with a reset time in the past, `ResolveFailureReasonAsync` returned `isUsageLimitedRequeue: true`, and `WorkerRunFailedHandler` immediately called `.Retry()` to requeue the issue.
This sent issues straight back into an active limit window because the reset time was often stale or inaccurate.

Claude Code also returns HTTP 429 for two fundamentally different conditions.
A *time-based* usage limit (5-hour or weekly rolling window) carries a human-readable reset time and self-heals when that time passes.
A *money-based* block — the credit pool is empty, the org monthly spend limit is reached, or the CLI ≥ 2.1.119 regression fires — carries no reset time at all and clears only when a human tops up credits, an admin raises the cap, or the next billing period begins.
Treating every 429 as time-based, by fabricating a reset time from a configured cooldown when none parsed, turned a money-based block into an endless re-dispatch loop against a wall: the issue re-queued every cooldown, and every attempt failed on the same 429.
The two conditions are indistinguishable from Foundry's side by any means *except* one: whether the result text contains a parseable reset time. Since #402 landed reset-time parsing for every observed message format, parse **success** is a reliable structured discriminator.

## Decision

A detected usage limit always pauses dispatch; there is no immediate requeue.
The `WorkerRunFailed.IsUsageLimitedRequeue` parameter and the two `.Retry()` branches in `WorkerRunFailedHandler` are removed.

The two 429 classes are told apart by **whether a reset time parses**, never by matching the message text:

- 429 (or an allowlisted `terminal_reason`) **with** a parseable reset time → `UsageLimited(resetsAt)`: the global `UsageLimitResetsAt` pause is set and the run fails with `FailureReason.UsageLimited`. Recovery is solely via auto-resume (`DispatchResumedHandler`) or manual resume, both of which retry usage-limited issues. `GlobalSettings.SetUsageLimitResetsAt` is extend-only and clamps to 7 days, so a stale reset time only ever extends an existing pause — it self-corrects on the next failure.
- 429 (or an allowlisted `terminal_reason`) **without** a parseable reset time → `CreditsExhausted`, which carries no reset time and drives an *indefinite* pause on the `ClaudeAccount` aggregate (`SpendState.Blocked`). No reset time is ever fabricated.

This extends [ADR 0058](0058-429-primary-usage-limit-signal.md): classification uses only structured signals (`api_error_status`, `terminal_reason`, and reset-time parse *success*), never the content of the result phrase. Class E in the taxonomy (the CLI ≥ 2.1.119 regression that shows org-limit wording even to non-org users) is direct evidence that the wording is unstable and must never be matched.

`SpendState` lives on the `ClaudeAccount` aggregate, independent of `CredentialValidity`. "Can this account spend?" is a property of the aggregate that already answers "can this account authenticate?"; the two are independent conditions that can hold simultaneously, and clearing one must leave the other in force.

## Considered Options

- **Keep the immediate-requeue branch for past-reset times** (status quo) — rejected: this was the direct cause of issues being stranded into active limits.
- **Add a separate auto-continue setting** — rejected: no scenario where you would resume dispatch but strand limited issues; the existing `AutoResumeOnUsageReset` already covers it.
- **Fabricate a reset time from a configured cooldown when none parses** — rejected: treats a permanent condition as transient and loops against a credit block; exponential backoff on that cooldown only slows the loop without ever stopping it.
- **Match credit/spend phrases in the result text** — rejected per ADR 0058: the string is fragile and localizable, and class E proves the wording already drifted between CLI versions.
- **Keep `FailureReason.UsageLimited` with a nullable `ResetsAt`** — rejected: `DispatchResumedHandler` re-queues via `EF.Functions.Like(FailureReason, "Usage limit reached%")`, so credit-blocked issues would share that prefix and an unrelated usage-limit auto-resume would re-queue every one at once, each failing immediately on the same 429.
- **Add `CreditBlocked` as a third `CredentialValidity` variant** — rejected: credit-blocked and auth-invalid are independent and can hold simultaneously, so whichever lands second would erase the first; the credential *is* valid, the account simply cannot spend.
- **Put credit state on `GlobalSettings`** — rejected: spend state belongs on the aggregate that owns authentication state, not in the `GlobalSettings` tunables bag.

## Consequences

- Contract change: `WorkerRunFailed.IsUsageLimitedRequeue` removed (acceptable at phase-1 POC maturity).
- Every time-based usage limit pauses dispatch globally; no fast-path retry exists.
- A non-limit 429 with no reset time classifies as `CreditsExhausted` and pauses indefinitely — the same conservative bias ADR 0058 chose; it clears on an operator resume or when the periodic credit probe finds the account can spend again.
- There is no `DefaultCooldownMinutes` setting: `GlobalSettingsSummary`, `IGlobalSettingsQueries`, `IContainerOutputParser.Parse` and the settings UI carry no cooldown field.
- Correctness of the split depends on #402's reset-time parser fixtures — a parse regression there would misroute a time-based limit into the credit path.

## Revisions
- 2026-08-13 — A 429 without a parseable reset time classifies as `CreditsExhausted` (an indefinite spend block) instead of a fabricated `DefaultCooldownMinutes` timed pause; folded in from the former 0046 record (#411).
