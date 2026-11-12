# Revision Trigger as a Polymorphic Union

> **2026-11-12 — Amended:** the Decision clause "the handler calls `ChecksPendingIssue.FixChecks(failingChecks)` when the counter is below `ChecksMaxFixPushes`" is corrected: the handler now dispatches a fix only when `ChecksPendingIssue.CanFixChecks` holds (below the cap and at least one failing check named).
> A red verdict naming none goes to review with `ChecksOutcome.StillFailing` and an empty list, and `FixChecks` rejects an empty list.
> A later decision changed the rule; the original clause neither drifted nor was false when written.

## Context

Revision lifecycle states (`revision_queued`, `revision_in_progress`, `revision_failed`) previously carried three flat columns — `review_comments TEXT`, `omitted_comment_count INTEGER`, `newest_consumed_comment_at TEXT` — to represent the reason a revision was dispatched. Adding `FailedChecks` as a second dispatch reason (from [ADR 0077](0077-gate-review-on-provider-checks-at-the-issue-level.md)) would have required nullable columns for each reason type and a separate discriminator column, producing a sparse row and coupling every future dispatch reason to a schema change.

## Decision

Replace the three flat columns with a single `revision_trigger TEXT` JSON column holding a discriminated union (`$type: "review_feedback" | "failed_checks"`). The union is modelled as an abstract record `RevisionTrigger` in `Foundry.Modules.Issues.Contracts` with sealed subtypes, serialised via `[JsonPolymorphic]` / `[JsonDerivedType]`. The `$type` tokens and the member names of each subtype are a stable wire+DB contract.

A companion `checks_fix_attempts INTEGER` column tracks how many `FailedChecks` revision dispatches have been pushed. When the `still_failing` verdict arrives and the counter is below `ChecksPendingIssue.ChecksMaxFixPushes` (= 2), the handler calls `ChecksPendingIssue.FixChecks(failingChecks)` which increments the counter and transitions to `revision_queued`; at or above the cap it calls `ToReview(StillFailing)` instead.

## Considered Options

**Flat columns per trigger type** — adds nullable columns for every future trigger variant; the row is sparse and schema changes are required for each new reason. Rejected.

**Separate revision_trigger_type discriminator + typed columns** — still requires column-per-variant; the discriminator duplicates what the JSON already provides. Rejected.

## Consequences

- Existing `review_feedback` rows are backfilled by migration `20261021180338_AddRevisionTrigger` using `json_object` to wrap the old `review_comments` array.
- The `$type` discriminator tokens are a durable contract: renaming them requires a paired data migration.
- New trigger variants (e.g. a manual re-dispatch with notes) can be added by adding a new `[JsonDerivedType]` and a new sealed record with no schema change.
