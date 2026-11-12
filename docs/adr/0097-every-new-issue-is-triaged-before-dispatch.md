# Every New Issue Is Triaged Before Dispatch

> **2026-11-12 — Amended:** the "Triage comes before blockers" clause in Decision, and the dependency-check position in the flow line above it, are replaced by [ADR 0102](0102-triage-runs-once-blockers-clear.md): a new issue with open blockers waits untriaged and is triaged once they close.

> **2026-11-12 — Amended:** the flow line in Decision gains a third route, added by [ADR 0103](0103-triage-defers-on-open-dependencies-by-linking-blockers.md): a `blocked` verdict moves the issue to `TriageBlockedIssue` and links the named blockers in the tracker.

## Context

Implement runs are expensive, and an issue that is too vague to implement wastes a whole run before anyone sees the gap. [ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md) names "is this issue described well enough to implement" as a judgment for Claude, and [ADR 0096](0096-awaiting-input-replies-detected-by-recorded-ids-and-branch-head.md) already gives Foundry a way to ask a person a question and resume work when they answer. The `triage` Worker Role needed a place in the issue lifecycle.

## Decision

Every newly detected issue is triaged once before its first implement run, and before the dependency check:

`Detected → TriageQueued → TriageInProgress →` either the dependency applier (`FreshQueued` or `Blocked`) when the verdict is `ready`, or `AwaitingInputIssue` with `ResumeTarget.Retriage` when it is `needs_input`.

- **Triage comes before blockers**, so a blocked issue that is under-specified has its questions answered while the blocker is still open. An unblocked issue goes straight to the queue without being judged a second time.
- **"Not ready" reuses awaiting input.** The verdict's `questions` are posted on the issue thread and answered there through ADR 0096's posting, registry exclusion, quiet period and reminder. When input arrives, the issue goes back to `TriageQueued`, and the triage prompt renders the clarification log. **Continue without answer** on a `Retriage` target goes to the dependency applier.
- **No automatic re-triage once an issue is ready.** An edit to a queued or blocked issue does not trigger triage again: the implement run reads the issue live, and such edits are usually refinements. The operator's **Re-triage** action, available from `FreshQueued` and `Blocked`, covers the rare edit that makes an issue worse.
- **A failed triage run has its own state.** `TriageFailedIssue(FailureReason)` retries transient failures through `TransientRetryService` and parks anything else, following [ADR 0014](0014-remove-immediate-requeue-always-pause.md). Its exits are **Retry triage**, **Skip triage** (to the dependency applier), closing the provider issue, and an edit to the issue once issue-body reads exist. `FailedIssue` is not reused, because its `Retry()` returns `FreshQueuedIssue` and would skip triage.
- **Triage is a worker like any other.** It counts against both the global slots and the Repository Worker Limit, and it is ordered Revision 0, Continuation 1, Triage 2, Fresh 3.

Revision, continuation and rebase-conflict runs never go through triage.

## Considered Options

- **Triage just before the first Fresh claim**: rejected because an under-specified issue that is blocked would sit for days without anyone being asked about it.
- **Triage as an opt-in, per repository or per issue**: rejected because it adds a setting and gives every issue two lifecycles to test.
- **A separate `NotReadyIssue` state with its own posting and reply detection**: rejected because it duplicates ADR 0096's mechanism and its fixes for double posting and self-replies.
- **Post the questions and re-triage only on a label change**: rejected because a reply in a comment, the natural response, would be silently ignored.
- **Re-triage on every body change while queued**: rejected because it costs a body read per waiting issue on every poll, and a triage run on every typo fix.
- **Fail open (a failed triage counts as ready)**: rejected as the default because a broken triage role would go unnoticed. It remains available on purpose as **Skip triage**.

## Consequences

- An issue that is never answered stays in `awaiting_input`, which is the intended outcome: no implement run is spent on it.
- Every new state has to be added to `InFlightWorkerCountQuery`, `WorkerCapacityAvailableHandler.WorkerRunIdExistsAsync`, `IssueStateRegistry` and the EF discriminators.
- Automatic splitting of issues that are too big is left out. Triage can raise size as a `needs_input` question, and the `outcome` enum can take a `too_big` value later without a breaking change.
