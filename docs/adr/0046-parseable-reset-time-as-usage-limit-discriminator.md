# Parseable Reset Time as the Usage-Limit Class Discriminator

## Context

Claude Code returns HTTP 429 for two fundamentally different conditions.
A *time-based* usage limit (5-hour or weekly rolling window) carries a human-readable reset time and self-heals when that time passes.
A *money-based* block — the credit pool is empty, the org monthly spend limit is reached, or the CLI ≥ 2.1.119 regression fires — carries no reset time at all and clears only when a human tops up credits, an admin raises the cap, or the next billing period begins.

Foundry treated every 429 as time-based: when no reset time parsed, it fabricated one from `GlobalSettings.DefaultCooldownMinutes` (see [ADR 0014](0014-remove-immediate-requeue-always-pause.md)'s extend-only clamp). A money-based block therefore produced an endless re-dispatch loop against a wall — Foundry re-queued the issue every cooldown, and every attempt failed on the same 429.

The two conditions are indistinguishable from Foundry's side by any means *except* one: whether the result text contains a parseable reset time. Since #402 landed reset-time parsing for every observed message format, parse **success** is now a reliable structured discriminator.

## Decision

Classify the two 429 classes apart by **whether a reset time parses**, never by matching the message text.

- 429 (or an allowlisted `terminal_reason`) **with** a parseable reset time → `UsageLimited(resetsAt)`. Behaviour is unchanged: a global `UsageLimitResetsAt` pause that self-heals or is cleared by auto/manual resume.
- 429 (or an allowlisted `terminal_reason`) **without** a parseable reset time → the new `CreditsExhausted` variant, which carries no reset time and drives an *indefinite* pause on the `ClaudeAccount` aggregate (`SpendState.Blocked`) that only an operator resume can clear.

`GlobalSettings.DefaultCooldownMinutes` and the fabricated-reset-time fallback are removed entirely. A 429 with no parseable time is now an honest indefinite block, not a fake timed pause.

This decision **extends [ADR 0058](0058-429-primary-usage-limit-signal.md)**: classification still uses only structured signals (`api_error_status`, `terminal_reason`, and now reset-time parse *success*), never the content of the result phrase. ADR 0058's prohibition on classifying by result text stands — class E in the taxonomy (the CLI ≥ 2.1.119 regression that shows org-limit wording even to non-org users) is direct evidence that the wording is unstable and must never be matched.

This decision **supersedes the `DefaultCooldownMinutes` fallback rationale in ADR 0014**. ADR 0014's core decision (a detected usage limit always pauses; no immediate requeue) stands; only its fabricated-fallback path — "when no recognised reset time is present … a configurable `DefaultCooldownMinutes` fallback is used" — is removed here.

`SpendState` lives on the `ClaudeAccount` aggregate, independent of `CredentialValidity`. "Can this account spend?" is a property of the aggregate that already answers "can this account authenticate?"; the two are independent conditions that can hold simultaneously, and clearing one must leave the other in force.

## Considered Options

- **Match credit/spend phrases in the result text to classify credit exhaustion explicitly** — rejected per ADR 0058: the string is fragile and localizable, and class E proves the wording already drifted between CLI versions. Parse success is a structural signal; phrase content is not.
- **Keep `FailureReason.UsageLimited` with a nullable `ResetsAt`** — rejected: `DispatchResumedHandler` re-queues via `EF.Functions.Like(FailureReason, "Usage limit reached%")`, so credit-blocked issues would share that prefix and an unrelated usage-limit auto-resume would re-queue every one at once, each failing immediately on the same 429. It also reinstates the fabricated-value sentinel this change removes.
- **Add `CreditBlocked` as a third `CredentialValidity` variant** — rejected: credit-blocked and auth-invalid are independent and can hold simultaneously, so folding them into one hierarchy means whichever lands second erases the first. It would also make the type name lie — the credential *is* valid; the account simply cannot spend.
- **Put credit state on `GlobalSettings`** — rejected on the logic-placement hierarchy: spend state belongs on the aggregate that owns authentication state, not in the `GlobalSettings` tunables bag.
- **Exponential backoff on the fabricated cooldown** — rejected: treats a permanent condition as transient; it slows the loop without ever stopping it.

## Consequences

- A future non-limit 429 with no reset time classifies as `CreditsExhausted` and pauses indefinitely. Accepted — the same conservative bias ADR 0058 chose, and recovery is one operator resume.
- No automatic way out of a credit block until the follow-up probe/auto-resume ships. Accepted — strictly better than the prior silent hourly loop.
- Contract changes: `GlobalSettingsSummary`, `IGlobalSettingsQueries`, and `IContainerOutputParser.Parse` all drop `DefaultCooldownMinutes` / `defaultCooldownMinutes`; the settings UI loses that field.
- Correctness of the split depends on #402's reset-time parser fixtures — a parse regression there would misroute a class-A limit into the credit path.
