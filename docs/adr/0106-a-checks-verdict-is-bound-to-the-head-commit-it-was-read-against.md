# A Checks Verdict Is Bound to the Head Commit It Was Read Against

## Context

The checks gate ([ADR 0077](0077-gate-review-on-provider-checks-at-the-issue-level.md)) reads a pull request's checks verdict once, before `review`, and records only its `ChecksOutcome`.
The verdict never named the commit it described, so a `review` issue kept "PR ready" after new commits reached its pull request.
A merge request rebased onto the default branch outside Foundry got a failing pipeline on its new head, while the issue stayed in `review` with `ChecksOutcome.Passed`.
The omission existed from the start: nothing in 0077 ties the outcome to a commit.
This decision amends 0077's Decision clause that every verdict "leads to `ReviewIssue`" as a final step, and the head source in [ADR 0105](0105-foundry-merges-pull-requests-itself-after-its-checks-gate.md)'s "Pinned head" clause.

## Decision

Every checks verdict carries the head commit it was read against, and a `review` issue returns to the checks gate when its pull request's head is no longer that commit.

- **Trigger is the head commit only.** A moved head sends the issue back to `checks_pending`; a newer red pipeline on an unchanged head does not.
- **Every outcome stores the commit.** `passed`, `still_failing`, `none_detected`, `unfinished` and `unreadable` all set it, and `ReviewIssue.VerifiedHeadSha` holds it.
- **One source: the PR status read.** The poller reads status before checks, so the stored commit equals or precedes the verdict's commit, and a race costs one extra re-check.
- **A null stored commit counts as moved.** Rows that predate the field re-check once; a null *current* head skips detection that poll.
- **Feedback first.** The head check runs only when the same review pass found no new or held-back feedback, because a fix run moves the feedback cutoff to its own start.
- **Fresh fix budget.** The return to `checks_pending` starts `ChecksFixAttempts` at 0, matching every other person-triggered round.
- **No rebase.** The issue goes straight to `checks_pending`; Foundry still rebases only after its own runs ([ADR 0087](0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md)).

## Considered Options

**A manual "recheck" button only:** rejected — the card stays wrong until a person notices, while code can see the head move ([ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md)).

**Re-read the verdict on every review poll and react to any red:** rejected — flaky reruns on an unchanged head would start fix runs, and GitHub would pay a checks read per review issue per poll.

**Store the commit only on `passed`:** rejected — with a null stored commit counting as moved, a `still_failing` issue would loop through checks and fix runs forever.

**Read the commit from the checks query:** rejected — `unreadable` has no checks response, so it would need a second source.

**Store the current head for pre-existing rows without re-checking:** rejected — it would record an unchecked, possibly failing head as verified.

## Consequences

- Fix runs follow each person's push whose new pipeline is red, bounded per push by `ChecksMaxFixPushes`.
  On a fast-forward-only project every merge forces other pull requests to rebase, which multiplies pipelines but not worker runs.
- 0105's pinned merge reads the stored commit; a race that stored an older commit refuses the merge as `head_moved`, which routes to `review`.
- `review` is no longer terminal for the checks gate: an issue can cycle `review` → `checks_pending` → `review` once per head move.
