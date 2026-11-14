# A Review Issue Whose Checks Pass on a Rerun Returns to the Checks Gate

## Context

[ADR 0106](0106-a-checks-verdict-is-bound-to-the-head-commit-it-was-read-against.md) re-checks a `review` issue only when its pull request's head commit moves ("Trigger is the head commit only").
Re-running or retrying a pipeline keeps the head, so a verdict recorded as red stays red after a person reruns the checks and they pass.
A merge request whose pipeline failed for an infrastructure reason (GitLab `ci_quota_exceeded`) kept "Checks red" on its card after every job was retried green.
This decision extends 0106's "Trigger is the head commit only" clause and adds a fix-budget rule beside its "Fresh fix budget" clause.

## Decision

A `review` issue whose stored `ChecksOutcome` is not `Passed` has its checks read again by the review pass on an unchanged head, and returns to the checks gate only when that read is green.

- **Green only.** A red, pending or unreadable read on the same head changes nothing; a moved head keeps 0106's path, which runs first.
- **Every non-passed outcome qualifies.** `still_failing`, `unfinished`, `unreadable` and `none_detected`; a `passed` verdict is never re-read, so the cycle ends.
- **Through the gate.** `ReviewIssue.RecheckAfterChecksPassed` moves the issue to `checks_pending` with `ChecksPendingReason.ChecksRerun`, and `ChecksPendingIssue.ResolveVerdict` stays the only router: merge-pending when Merge When Checks Pass is on, otherwise review.
- **Exhausted fix budget.** The rerun round starts `ChecksFixAttempts` at `ChecksMaxFixPushes`, so a red second read (a flaky job between the two reads) routes to review and starts no fix run. Only a new commit earns new fix runs.
- **Webhook nudge.** A finished pipeline or check suite that no `checks_pending` issue owns nudges the review pass of the `review` issue that owns the pull request or branch.

## Considered Options

**A manual "recheck" button:** rejected for the same reason 0106 rejected it — code can see the rerun turn green ([ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md)).

**React to any verdict on a rerun:** rejected — a flaky red rerun would start fix runs on unchanged code, the risk 0106 rejected.

**Resolve the green read inside the review pass:** rejected — it needs a second merge-or-review router beside `ResolveVerdict`.

**Reset the fix budget to 0, as a head move does:** rejected — no one pushed, so a red second read would spend fix runs on a flake.

## Consequences

- On GitHub, each non-green `review` issue costs one checks read per review poll.
- A `review` issue can now leave for the gate without new commits; its pane names the rerun rather than new commits.
