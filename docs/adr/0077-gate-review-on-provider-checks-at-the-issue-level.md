---
reaches: structure
---

# Gate Review on Provider Checks at the Issue Level

## Context

A worker exits once it has pushed and opened a PR, and `WorkerOutcomeResolver` maps any open PR to `Review` regardless of CI status ([ADR 0060](0060-mr-state-first-outcome-resolution.md)). Waiting for CI was left to whatever the operator-editable worker prompt says, so a worker that skipped it, ran out of turns, or used a different template delivered a red PR into review with no signal.

## Decision

Foundry verifies the provider's checks itself, as an issue-lifecycle gate after the worker run has ended. The worker does not watch checks: it holds no provider credential and Foundry publishes its branch ([ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md)).

- An open PR moves the issue to `ChecksPendingIssue` (lifecycle group: Waiting), after the rebase step of [ADR 0087](0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md), not straight to `ReviewIssue`. `RepositoryPoller` reads the overall verdict — GitHub `statusCheckRollup.state`, GitLab `head_pipeline.status` — on each poll.
- Green, no checks after a fixed grace window, checks unfinished after a fixed upper bound, checks unreadable (401/403), and checks still red after a bounded number of fix runs all lead to `ReviewIssue`, which records which one as a `ChecksOutcome` variant together with the head commit the verdict was read against. A `review` issue whose pull request head moves returns to `checks_pending` ([ADR 0106](0106-a-checks-verdict-is-bound-to-the-head-commit-it-was-read-against.md)).
- Red within the fix budget re-queues the issue through the existing revision states, with a `FailedChecks` trigger variant alongside `ReviewFeedback`.

## Considered Options

**Hold the WorkerRun open until CI settles:** reuse the resolver's `Indeterminate` retry loop. Rejected — CI status belongs to the PR, which outlives the container; this would occupy a concurrency slot with no container behind it and conflate the container timeout with CI duration.

**Worker-side instruction or `Stop` hook only:** rejected — nothing verifies agent compliance, and a hook that blocks exit while red runs to `--max-turns` or the container timeout instead of terminating.

**Required checks only:** rejected — a repository without branch protection has no required checks, so every PR would read green; GitLab has no per-job required flag.

**Separate `ChecksFix` queued/in-progress states:** rejected — duplicates the branch/PR revision flow and its dashboard, claim, and reconcile wiring; only the trigger differs.

## Consequences

- Every path into review from a worker passes the gate, including revision runs.
- An empty verdict and an unreadable verdict are distinct; a token lacking check-read permission surfaces as `ChecksOutcome.Unreadable` rather than passing as "no CI".
- `checks_pending` has automatic exits on every branch (grace window, upper bound, fix-attempt cap), so it never absorbs an issue.

## Revisions
- 2026-11-12 — The worker-side instruction to drive checks green before exiting was removed when workers stopped holding a provider credential ([ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md))
- 2026-11-13 — A checks verdict records the head commit it was read against, and a moved head returns a `review` issue to `checks_pending` ([ADR 0106](0106-a-checks-verdict-is-bound-to-the-head-commit-it-was-read-against.md))
