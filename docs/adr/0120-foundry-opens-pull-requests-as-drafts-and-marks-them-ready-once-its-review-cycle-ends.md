---
reaches: external-contract
---

# Foundry Opens Pull Requests as Drafts and Marks Them Ready Once Its Review Cycle Ends

## Context

Foundry's pull requests were visible to people as ready for review from the moment they opened, while Foundry's checks gate, checks-fix runs and machine review (ADRs 0077, 0113) were still running and could push further commits. Providers have a native draft state: GitHub requests no CODEOWNERS review on a draft and refuses to merge it; a GitLab draft merge request cannot be merged until the Draft flag is removed. Issues #592 and #641 rejected a draft as a failure marker; this uses it as a not-yet-reviewed marker.

## Decision

Foundry opens every pull request as a draft and marks it ready once, when the issue first reaches `ReviewIssue` or `MergePendingIssue`.

- **Opening** — GitHub create sends `draft: true`; GitLab create prefixes the title with `Draft: `. There is no setting.
- **Marking ready is reconciled from observed state** — the repository poller's `ReviewIssue` pass and `MergeStep` read the pull request's draft state, which already runs there, and mark it ready when it is a draft. A failure retries on the next poll, or, in `MergeStep`, counts as a transient failure that fails open to `review` ([ADR 0105](0105-foundry-merges-pull-requests-itself-after-its-checks-gate.md)). An issue entering `ReviewIssue` raises `IssueEnteredReview`, which nudges that pass at once ([ADR 0085](0085-webhooks-nudge-the-poll-loop.md)); the nudge only starts the pass, so a lost nudge leaves marking ready to the backstop poll.
- **GitHub** marks ready with the GraphQL mutation `markPullRequestReadyForReview(pullRequestId)`. **GitLab** reads the merge request's title and removes only a leading `Draft:`, `[Draft]` or `(Draft)` prefix, so a person's title edits survive ([ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md)'s reasoning on the body).
- **A one-way latch tied to the pull request** — the issue records the URL it marked ready, so a new pull request after a fresh retry starts unmarked and a person's re-draft is never undone. `MergeStep` resolves `Skipped("draft")` to `review` for a re-drafted, already-marked pull request instead of attempting the merge.
- **Foundry never converts a pull request back to draft**, and a person's comment on a draft is actionable feedback as for any pull request ([ADR 0070](0070-any-human-comment-is-actionable-feedback.md)).

Rejected: event-driven marking from `IssueInReview` and `IssueMergePending` (a dead-lettered call strands the draft with only an operator exit, and the merge event can race the merge step); a boolean latch (a stale value is representable); re-drafting each cycle (repeats review requests and fights a person); a per-repository setting (no repository needs one; the failure is fail-open).

## Consequences

A repository whose CI skips draft pull requests reads `NoneDetected` while the pull request is a draft, skips the checks-fix loop and never auto-merges; its issues reach a person through `ReviewIssue`. A per-repository opt-out is the follow-up when one is needed.

## Revisions
- 2026-11-27 — entering review nudges the review pass, so marking ready no longer waits for the backstop poll (#1366)
