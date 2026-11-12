# Triage Runs Once an Issue's Blockers Clear

## Context

[ADR 0097](0097-every-new-issue-is-triaged-before-dispatch.md) triages every new issue before the dependency check, so an under-specified blocked issue gets its questions answered while the blocker is still open. This decision replaces that ordering clause. In practice a blocked issue is judged against code its blocker has not written yet: the triage run cannot see the types the blocker adds, asks whether the blocker has merged, and gets answers that can go stale when the blocker lands. Akraborg #1311, blocked by #1308, was triaged that way and asked four questions, all of which the code, the decision records or the tracker already answered.

## Decision

A new issue with open blockers waits untriaged, and is triaged once its blockers close.

- `TriageQueued` with open blockers becomes `TriageBlockedIssue`. Both the poll-path dependency pass and a claim-time blocker re-check on triage candidates apply this, the same way the Fresh claim re-checks. The claim-time check covers the gap between detection and the first dependency pass.
- `TriageBlockedIssue.Unblock()` returns `TriageQueuedIssue`. `BlockedIssue.Unblock()` still returns `FreshQueuedIssue`. Each blocked state type can only unblock to its own next state. A null triage verdict cannot tell the two apart, because **Skip triage** also produces a `BlockedIssue` without a verdict.
- A run already in `TriageInProgress`, and an issue in `AwaitingInput` with a `Retriage` target, are not interrupted. When they resume, the issue lands in `TriageQueued`, where the first rule applies.
- **Skip triage**, **Continue without answer** and **Re-triage** keep the routes ADR 0097 gives them.

## Considered Options

- **Triage before blockers, judging as if they had landed** (ADR 0097's ordering, with a prompt line): rejected because the run still reasons about code that does not exist and about the blocker's description rather than what landed.
- **Triage before and a re-check after the blockers clear**: rejected because two runs per blocked issue mostly repeat the same judgment.
- **One `BlockedIssue` carrying an "unblocks to" value**: rejected because routing becomes a runtime switch on data where a state type makes the wrong route unrepresentable.

## Consequences

- Waiting for answers now happens after the blocker closes instead of alongside it, so a blocked under-specified issue reaches implementation later. ADR 0097 rejected "triage just before the first Fresh claim" for this reason. This decision accepts that delay in exchange for a judgment made against the code as it actually is.
- `TriageBlockedIssue` must be added to `IssueStateRegistry`, the EF discriminators, the issue queries and the dashboard, as ADR 0097's Consequences list for every new state. It holds no worker slot.
