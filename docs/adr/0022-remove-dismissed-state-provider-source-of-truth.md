# Remove `dismissed` State — Provider as Source of Truth for Issue Closure

## Context

`dismissed` was a terminal issue state produced by `UnchangedIssue.Complete()`, intended to record
that the user agreed no code changes were needed.
In practice, `UnchangedIssue.Complete()` had no production callers, making `dismissed` a dead state.
A separate problem existed alongside it: resting-state issues that stalled (e.g. repeated failures
that the operator gave up on) had no escape path — they accumulated in the active tracked set
indefinitely with no automatic removal.

## Decision

Remove `dismissed` entirely.
Make the provider the source of truth for issue closure: when an issue's Foundry trigger label is
removed or the issue is closed on the provider, both surfaces as the issue disappearing from the
`?labels=foundry&state=open` fetch.
On each poll cycle, the poller emits a new `ProviderIssueUntracked` integration event for any
tracked issue absent from the latest fetch.
A `ProviderIssueUntrackedHandler` hard-deletes the tracked record for resting states: `detected`,
`queued`, `blocked`, `failed`, `continuable_failed`, `revision_failed`, `revision_queued`, and
`continuation_queued`.
`completed` and `unchanged` are preserved — completion wins over provider closure.
`in_progress`, `revision_in_progress`, and `review` are also preserved — a live worker is running
or the issue is under review; worker cancellation is out of scope.

**AC-wording deviation:** the issue description referred to a `ProviderIssueClosedHandler` that
"untracks instead of transitioning to `dismissed`".
In the actual codebase, `ProviderIssueClosedHandler` completes `ReviewIssue`s on issue close and
was left unchanged.
The untrack rule was implemented as a separate event (`ProviderIssueUntracked`) and handler
(`ProviderIssueUntrackedHandler`) to avoid conflating two distinct intents.

## Considered Options

- **Keep `dismissed` as a terminal state** — rejected: duplicates the provider-owned closed /
  won't-do concept, drifts from provider truth, and its only producer had no callers.
- **Add a distinct `abandoned` state for given-up failures** — rejected: same provider-boundary
  objection; hard-deleting on untrack is simpler and keeps Foundry's tracked set aligned with the
  provider's label-filtered view.

## Consequences

The in-Foundry trace of externally-closed issues is lost on hard delete — accepted for a POC with
disposable contracts; the provider retains the closed issue.
Hard delete is irreversible, mitigated by the resting-state-only guard and `completed`/`unchanged`
preservation.
Edge case: an issue closed on the provider and then reopened with the trigger label still present is
re-detected as a fresh issue (acceptable — no history to recover).
