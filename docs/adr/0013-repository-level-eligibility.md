# Repository-Level Eligibility Instead of Per-Issue Ineligible State

> **Partially superseded.** The "branch-protection check runs every cycle" clause applies to the
> branch-rules GET only. The write-probe half is superseded by [ADR 0054](0054-split-eligibility-cadence.md), which makes write probes
> event-triggered (repository add, manual re-check, credential update) and persists the last
> verdict as `WriteProbeVerdict` on `MonitoredRepository`.

## Context

Branch protection is a property of a repository, yet eligibility was modelled on the issue. The poll made one branch-protection API call per repo, then fanned the result out as an `IssueEligibilityChecked` integration event to every issue, and `IssueEligibilityCheckedHandler` transitioned each one into or out of an `IneligibleIssue` lifecycle state — every issue storing its own copy of the same repo-wide violations. The check was then repeated, redundantly, at claim time in `WorkerCapacityAvailableHandler` (a second live API call per claim), and recovery was per-issue via `POST /api/issues/{id}/retry-eligibility`. A repository with no tracked issues had no visible eligibility at all, because the result only ever lived on issues.

## Decision

Eligibility moves to the `MonitoredRepository` aggregate and the per-issue `IneligibleIssue` state is removed.

- `MonitoredRepository` carries a `RepositoryEligibility` value object modelled as a discriminated union: `Eligible`, `Ineligible(violations)` (non-empty, user-actionable), `Unreachable` (provider API could not be reached — transient). This makes invalid combinations (eligible-with-violations, ineligible-without) unrepresentable.
- The branch-protection check becomes an unconditional repo-level poll step that runs every cycle regardless of issue count, plus a synchronous check at repository creation and a manual per-repo "re-check" endpoint (replacing the per-issue retry endpoint). A fixed repo heals automatically on the next poll.
- Issue-ineligibility is derived, not stored. Issues stay in their natural states (`Detected`/`Queued`/`Blocked`); eligibility gates only dispatch. `WorkerCapacityAvailableHandler` filters the claimable-issue selection to issues whose repo is `Eligible`, reading stored state via a Monitoring read-query contract (`IRepositoryEligibilityQuery`) — no live API call at claim time. Detection, dependency reconciliation, and review polling continue on ineligible repos, and already-running workers are unaffected.
- The Issues read model joins repo eligibility to show a warning marker/banner on queued issues whose repo is ineligible; repository cards in settings show validity and the actionable violation list.

## Considered Options

- **Keep `IneligibleIssue`, drive its transitions from repo-level events** — rejected: reintroduces duplicated violation storage and per-issue state churn for a condition that is wholly a repository property, and conflates a derived condition with the lifecycle state machine.
- **Polymorphic `MonitoredRepository` aggregate (`Eligible`/`Ineligible` variants, TPH)** — rejected: eligibility is a frequently-flipping attribute that does not change repository behaviour (an ineligible repo still polls and detects), so a value object is the right granularity; aggregate variants are reserved for lifecycle states with distinct behaviour.
- **Event-driven denormalized eligibility copy in the Issues module** — rejected: only pays off for module-level deployment independence the monolith does not need, and adds a second copy that can drift from the authoritative repo state.
- **Treat "unreachable" as an ineligibility violation (status quo)** — rejected: it is not user-fixable, so listing it among actionable violations is misleading; a separate `Unreachable` variant keeps the violation list strictly actionable.

## Consequences

- Requires an EF Core migration: convert existing `IneligibleIssue` rows to `DetectedIssue` (re-derived on the next poll), drop the `IneligibleIssue` discriminator and violation columns, and add eligibility storage to `MonitoredRepository`.
- `IssueEligibilityChecked` / `IssueEligibilityCheckedHandler`, the per-issue retry endpoint, and the claim-time `IBranchProtectionValidator` call are removed; `IBranchProtectionValidator` is replaced for consumers by stored-state reads via `IRepositoryEligibilityQuery`.
