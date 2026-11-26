---
reaches: structure
---

# Foundry Rebases a Mergeable Pull Request Before Marking It Ready

## Context

The post-run rebase ([ADR 0087](0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md)) runs before the checks gate and the review, so a pull request whose default branch moved during that window reaches a person already behind and, on a fast-forward or semi-linear repository, unmergeable. [ADR 0087](0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md) leaves pull requests waiting in review un-synced, and the catch-up queue ([ADR 0111](0111-foundry-serializes-catch-up-rebases-for-pull-requests-it-merges.md)) serves only pull requests Foundry merges itself. Rejected: re-syncing pull requests while they wait in review (a pipeline per sibling merge per waiting pull request, and the person chooses the merge order); entering review first and re-syncing from the review pass (it marks a behind pull request ready); rebasing only when the provider or the repository's merge policy demands it (GitLab's merge status is computed asynchronously and reads `unchecked`, and a cached policy is a second reader that can drift).

## Decision

A pull request about to enter review that a person can merge as it stands — a green or check-less verdict, and a review that approved, approved for a person, or failed open — first passes through `rebase_pending` with reason `BeforeReady`, through the existing rebase step, whatever the repository's merge policy; `RebaseAfterRun` is the switch. Up to date, it enters review unchanged; rebased, it re-runs the checks gate and its approval is carried when the patch-id is unchanged; conflicting, it takes the existing `RebaseConflict` path. Every attempt that is not up to date uses one of three rounds, which only new code from review findings, a checks fix or a person resets; past the cap the step only reads whether the branch is behind and the pull request enters review, badged `fell-behind` when it is. The pull request is marked ready ([ADR 0120](0120-foundry-opens-pull-requests-as-drafts-and-marks-them-ready-once-its-review-cycle-ends.md)) only on entering review, so a person is notified only of an up-to-date branch, except at the cap.

## Consequences

- One extra pipeline per behind handoff, at most three per episode; a repository that merges behind branches pays it with no mergeability gain.
- Pull requests that fall behind while waiting in review are still not re-synced.
- A replacement for the provider rebase (#825) must keep the `BeforeReady` path beside `CatchUp`.
