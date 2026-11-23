---
reaches: external-contract
---

# Foundry Merges Pull Requests Itself After Its Checks Gate

## Context

A repository can opt in to having the pull requests Foundry opens merged without a person.
Both providers ship a native auto-merge that Foundry could arm when it opens the pull request (GitHub `enablePullRequestAutoMerge`, GitLab `merge_when_pipeline_succeeds`).
Foundry already runs its own checks gate over every check on the head commit ([ADR 0077](0077-gate-review-on-provider-checks-at-the-issue-level.md)), and the branch ruleset it writes ([ADR 0084](0084-branch-rules-written-by-a-separate-server-side-token-on-operator-action.md)) declares no required status checks.

## Decision

Foundry merges the pull request itself, through the provider's merge API, as a code-owned side effect ([ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md)).
It never arms the provider's native auto-merge.

- **Opt-in** — the per-repository `MergeWhenChecksPass` toggle, default `false`, read live by the merge step.
- **Gate** — only a `Passed` checks outcome routes to the new `merge_pending` state.
  `NoneDetected`, `Unfinished`, `StillFailing`, and `Unreadable` go to `review` as before.
- **Pinned head** — the checks verdict carries the head commit read from the pull request status for every outcome ([ADR 0106](0106-a-checks-verdict-is-bound-to-the-head-commit-it-was-read-against.md)), and the merge call passes it as the provider's `sha` guard.
  That commit can precede the one the verdict describes, so a race refuses the merge as `head_moved` rather than merging unchecked work; a moved head refuses the merge.
- **Fail open** — a refusal, an authorization failure, or an exhausted transient-retry budget resolves to `review` with a persisted `MergeOutcome` of `Skipped(reason)`.
  A merged pull request completes the issue directly.
- **Person in the loop** — once a person's review feedback has driven a revision, that issue no longer merges automatically.
- **Method** — the first method the repository allows of squash, rebase, merge commit.
  On GitLab, `squash` is requested unless the project's `squash_option` is `never`, and the project's merge method decides the rest.

## Considered Options

**Arm the provider's native auto-merge when the pull request opens** — the provider waits only for *required* checks, a filter ADR 0077 rejected.
On a repository whose only protection is Foundry's ruleset, nothing is required.
GitHub then refuses to arm a pull request already in `CLEAN` status, and GitLab has merged a request accepted before its pipeline existed.
The merge would also happen outside Foundry's view, racing the checks-fix revision loop.
Rejected.

**Arm native auto-merge where protection exists, merge directly otherwise** — two merge paths, plus provider-state detection to choose between them.
Rejected.

## Consequences

- The setting is named `MergeWhenChecksPass` rather than "auto-merge", because both providers use "auto-merge" for the native feature this ADR rejects.
- A repository that requires up-to-date branches, or a GitLab project using fast-forward merges, refuses the merge of a pull request that fell behind the default branch; that refused merge joins a serialized catch-up queue that rebases one pull request at a time ([ADR 0111](0111-foundry-serializes-catch-up-rebases-for-pull-requests-it-merges.md)).
- On GitLab, Foundry's branch rules allow only Maintainers to merge into the default branch, so a PAT owned by a Developer is refused on every merge.
  No check runs when the toggle is enabled, and the refusal reason on the card is the signal.

## Revisions
- 2026-11-13 — The pinned head comes from the pull request status read for every outcome rather than from the checks read, so a race refuses the merge as `head_moved` ([ADR 0106](0106-a-checks-verdict-is-bound-to-the-head-commit-it-was-read-against.md)).
- 2026-11-14 — A merge refused because the pull request is behind joins a serialized catch-up queue instead of going to `review` as `Skipped` ([ADR 0111](0111-foundry-serializes-catch-up-rebases-for-pull-requests-it-merges.md)).
