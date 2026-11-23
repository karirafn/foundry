---
reaches: structure
---

# Foundry Serializes Catch-Up Rebases for Pull Requests It Merges

## Context

[ADR 0105](0105-foundry-merges-pull-requests-itself-after-its-checks-gate.md) merges a pull request once its checks gate passes, pinned to the verified head, and sends every refusal to `review`.
A repository that requires up-to-date branches (GitHub `strict_required_status_checks_policy`) or merges with semi-linear history (GitLab `rebase_merge`) refuses a pull request that fell behind while its checks ran.
When several Foundry pull requests finish together, every merge after the first is refused, and each needs a person.
Catching each one up independently would rerun CI on every remaining pull request after every merge: N(N−1)/2 extra pipelines for N pull requests finishing together.
This replaces 0105's Consequences clause that a pull request falling behind goes to `review` as `Skipped`.
It also partly covers the re-sync [ADR 0087](0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md) left out.

## Decision

A merge refused because the pull request is behind joins a per-repository catch-up queue, and Foundry rebases one queued pull request at a time.

- **Merges are not queued.**
  Every merge-pending pull request attempts its pinned merge as soon as it is ready; a merge costs no CI.
- **Behind is decided by ancestry, after a refusal.**
  A refused merge whose branch does not contain the default-branch head (`IIssueProvider.IsAncestorAsync`) joins the queue.
  The provider's refusal applies the repository's own policy, so a repository that merges behind branches never rebases.
- **Rebases are serialized.**
  A per-repository `MergeQueue` aggregate in Monitoring owns the queue, persisted as `pending_merges` rows ordered by when each entry first joined.
  Only the head rebases, through the existing `RebasePending` pipeline (`RebasePendingIssue` reason `CatchUp`), then the checks gate, then the pinned merge.
- **An up-to-date pull request goes first.**
  The head waits to rebase while another auto-merge pull request already contains the default-branch head and is still in `checks_pending` or `merge_pending`.
  Only a confirmed up-to-date answer holds the queue.
- **Only code-driven steps hold a place.**
  A worker run (rebase conflict, checks fix, review feedback), a move to `review`, or a closed pull request releases the entry.
  The pull request rejoins at the back when it next reaches `merge_pending`.
- **Bounded.**
  An entry gets three catch-up rounds; the next refusal resolves `Skipped(fell_behind)`.
  A catch-up whose rebase resolves `Skipped` goes to `review` as `Skipped(catch_up_failed)`, keeping the rebase reason.
- **One switch for rewriting branches.**
  With `RebaseAfterRun` off, a refused pull request that is behind goes to `review` as `Skipped(not_mergeable)`, as before.

## Considered Options

**Catch up every refused pull request at once** — simplest, one new transition.
Rejected: N pull requests finishing together cost N(N−1)/2 extra pipelines.
The queue costs N−1, the floor when each needs one pipeline on the new default branch.

**Rebase every open auto-merge pull request after each merge** — rejected for the reason in 0087: it spends CI on pull requests that may never merge.

**A queue over merges, first in first out** — rejected: a pull request already on the default-branch head would wait behind a stale one.
Its pipeline is then wasted when the stale one merges first.

**The provider's native queue (GitHub merge queue, GitLab merge trains)** — rejected for the reason in 0105: the merge happens outside Foundry's view.
GitLab merge trains also need the Premium tier and merged results pipelines.

**GitLab's automatic rebase before merge** — rejected: GitLab does not rerun the pipeline after that rebase, so the merged commit was never checked.

**Hold the head's place through worker runs** — rejected: one conflicting pull request would stall the repository's merges for a worker run's length.

## Consequences

- The first pipeline after a worker's push still runs on a possibly stale base.
  It is needed to start a checks-fix run before the pull request reaches the queue.
- A pull request can stay `merge_pending` through another pull request's whole pipeline, so the dashboard must show why it waits.
- `pending_merges` rows outlive `merge_pending`: a row now lives through the head's `rebase_pending` and `checks_pending` catch-up.
