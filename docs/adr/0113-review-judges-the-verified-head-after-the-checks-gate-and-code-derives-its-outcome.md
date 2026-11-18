# Review Judges the Verified Head After the Checks Gate, and Code Derives Its Outcome

## Context

The `review` Worker Role, planned since [ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md) and [ADR 0095](0095-worker-roles-ship-in-the-worker-image.md), judges whether an implementation pull request is correct and meets its issue.
It needed a place in the issue lifecycle, a verdict shape, and a way to feed the revision flow.
A review verdict describes one commit, the same property [ADR 0106](0106-a-checks-verdict-is-bound-to-the-head-commit-it-was-read-against.md) gives a checks verdict.
A `ReviewFeedback` revision sets `HasPersonFeedback`, which permanently stops automatic merging ([ADR 0105](0105-foundry-merges-pull-requests-itself-after-its-checks-gate.md)).
This decision also replaces [ADR 0097](0097-every-new-issue-is-triaged-before-dispatch.md)'s dispatch tier order.

## Decision

The review run happens after the checks gate, on the head the gate verified, and Claude returns assessments from which code derives the outcome.

- **Placement** — a `Passed` or `NoneDetected` checks outcome goes to `review_queued` when the repository's `ReviewPullRequests` toggle is on (default on).
  `StillFailing`, `Unfinished` and `Unreadable` go to `review` as before.
  The run reviews the gate's verified head, and the verdict records that commit.
- **Assessments, not an outcome** — the verdict carries `summary`, `findings[]` (`path`, `line?`, `severity: blocking | advisory`, `category`, `rule?`, `body`), a `dimensions` object (correctness, criteria, tests, security, quality, design, conventions; each `clear | findings | not_applicable` with a note), `criteria[]` and `tests`.
  The schema has no `outcome` field.
- **Code derives the outcome** — `changes_requested` when any finding is blocking, `tests.assessment` is `missing`, or a criterion is `unmet`.
  `approved_for_person` when a criterion is `not_verifiable` or the issue has no acceptance-criteria heading.
  `approved` otherwise.
- **Code checks the shape of the judgment** — a verdict is `verdict_invalid` when a dimension's status disagrees with its findings, when a blocking `quality`, `design` or `conventions` finding cites no rule (a numbered Principle line code validates against the role catalog, or a `{ file, quote }` repository convention), when `criteria[]` misses, repeats or invents an `AC` id code extracted, or when `coveringTests` names a path the changed-file classifier does not class as a test.
- **Routing** — `approved` goes to `merge_pending` when `MergeWhenChecksPass` is on, otherwise to `review`.
  `approved_for_person` always goes to `review`.
  `changes_requested` starts a revision with the new `RevisionTrigger.ReviewFindings` while `ReviewFixAttempts` (on the `Issue` base) is below 2, and goes to `review` once that budget is spent.
  A person-triggered round resets the budget.
- **An approval carries across an unchanged change** — each verdict stores the zero-context patch-id of the reviewed diff (`git diff -U0 <merge-base>..<head> | git patch-id --stable`).
  When a later review's prepare step reports the same id as the issue's latest `approved` or `approved_for_person` verdict, code completes that review as carried, bound to the new head, without starting Claude, and routes as the original verdict did.
  A `changes_requested` verdict never carries.
- **A machine verdict is not person feedback** — a `ReviewFindings` revision leaves `HasPersonFeedback` unset, so automatic merging survives a review-driven fix.
- **Failure fails open to a person** — a transient failure retries from `ReviewFailedIssue` on a bounded cooldown.
  Any other failure, or an exhausted retry, goes to `review` with `ReviewOutcome.Skipped(reason)`.
  A failed review never reaches `merge_pending`.
- **Dispatch order** — Revision 0, Review 1, Continuation 2, Triage 3, Fresh 4.
- **Dispatch by verdict-only profile, never by role name** — the outcome resolver delegates restricted, branchless runs to a role-keyed resolver, as [ADR 0098](0098-judgment-roles-run-restricted-with-code-supplied-issue-content.md) requires of the entrypoint and container spec.
- **Principles** — the review role receives the same Principle Sections as `implement`, pinned equal by a test.

## Considered Options

- **Review before the post-run rebase**: rejected because the rebase moves the head at once, so the verdict would describe a commit that is no longer the pull request's head.
- **Review in parallel with the checks gate**: rejected because it needs a join state and a rule for which gate's fix run invalidates the other's verdict.
- **Claude returns the outcome, code cross-validates it**: rejected because every disagreement becomes a rerun, and the merge-blocking rule would live in a prompt instead of in tested code.
- **Map findings onto `ReviewComment` and reuse `ReviewFeedback`**: rejected because it sets `HasPersonFeedback` and records a machine verdict as a person's.
- **Re-review every new head**: rejected because each merge on a busy repository would send every pull request in [ADR 0111](0111-foundry-serializes-catch-up-rebases-for-pull-requests-it-merges.md)'s catch-up queue through another review of an unchanged change.
- **Skip review when the rebase reason is `CatchUp`**: rejected because a conflicting catch-up becomes a `RebaseConflict` revision that rewrites code, so the reason alone cannot tell whether the change changed.
- **Carry by the default patch-id**: rejected because its hunk context lines are hashed, so a clean rebase onto a default branch that changed near a hunk changes the id.
  A scratch spike showed the default id changing on a clean rebase while the `-U0` id stayed equal, and the `-U0` id changing after a hand-resolved conflict.
- **Fail closed into `ReviewFailedIssue` awaiting Retry or Skip**: rejected because its only exit would be a person, while the pull request is already green.
- **A separate security role**: rejected because it doubles runs per pull request and needs a join of two verdicts on one head; security is a skill inside the review role.

## Consequences

- Taste cannot block a pull request: a finding without a rule code can check is advisory.
- A repository whose test layout the built-in classifier misreads gets a reviewer judgment on the test flag, not a hard failure.
- Every new review state joins the lists ADR 0097's Consequences name: `InFlightWorkerCountQuery`, `WorkerCapacityAvailableHandler.WorkerRunIdExistsAsync`, `IssueStateRegistry` and the EF discriminators, plus the closed-issue and closed-pull-request handlers.
