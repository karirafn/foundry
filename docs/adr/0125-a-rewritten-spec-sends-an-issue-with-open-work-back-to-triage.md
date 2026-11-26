---
reaches: hidden-invariant
---

# A Rewritten Spec Sends An Issue With Open Work Back To Triage

## Context

Triage judged an issue once. A description rewritten after the pull request existed went straight to a revision with no readiness check. The body was not watched on a pull-request thread because ticking acceptance-criteria checkboxes edits it. A re-triage through the fresh path recomputes the branch name from the title and kind, which can open a second pull request.

## Decision

- Each triage run records `TriagedSpecHash`: the hash of the body it was given, with every task-list marker normalized to `[ ]`, so ticking a checkbox is not a rewrite.
- While an issue waits in `RevisionQueued`, `RevisionBlocked`, `ContinuationQueued`, `ContinuationBlocked`, `Review` or `RevisionFailed`, a live body whose normalized hash differs and outlasts the quiet period sends it to triage. Running work and the code-owned pipeline are not interrupted.
- The issue's `OpenWork` (its branch, and its pull request when it has one) is carried through triage. A ready verdict resumes it on that branch through the blocker check, as a `SpecChanged` revision with a pull request and as a continuation without one; the branch name is never recomputed for an issue with open work.

## Considered Options

- **Never watch the body on a pull request:** rejected; a rewritten spec bypassed triage.
- **A size threshold for a rewrite:** rejected; no threshold is justified, and a one-line criterion change can move the design.
- **Re-triage through the fresh path:** rejected; a changed title or kind opens a second pull request.

## Consequences

- A typo fix re-triages the issue.
- A rewrite during merge is not acted on; an auto-merge can merge the old spec.

## Revisions
- 2026-11-25 — Open work's pull request is optional: a continuation state sends its branch alone to triage, and a ready verdict resumes branch-only open work as a continuation on that branch (#1277).
