---
reaches: hidden-invariant
---
# Issue Transitions Are Logged From the New State, in the Same Transaction

## Context

Nothing durably recorded an issue's transitions: state-change domain events carry only ids, and `outbox_messages` is pruned.
The pane's step timeline and the retrospective role's run selection both need per-issue history with causes (revision trigger, failure category, checks outcome).
Rejected: a projection over `IIssueStateChanged` events, which carry no cause.
Also rejected: durations from `WorkerRun` alone, which leave code-run steps blank.
Also rejected: leaving history to the retrospective role, which would create a second store of the same transitions.

## Decision

Every added `Issue` entity writes one `issue_step_transitions` row in the same transaction: lifecycle step, state, round, entered at and cause.
Each state type declares its cause, so a new state cannot compile without one.
Detection is covered because the row is written from any added issue, not from `TransitionAsync` alone.
Rows are kept for the issue's lifetime and are not pruned with the outbox.

## Consequences

Every write path into the issues table produces history, whether or not its caller knows about the log.
The retrospective role selects runs from structured rows rather than transcripts.
The table has no foreign key to the issues table, because each transition removes and re-adds the issue row under one id.
