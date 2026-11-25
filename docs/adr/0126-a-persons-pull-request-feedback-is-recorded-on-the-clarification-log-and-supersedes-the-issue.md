---
reaches: module-contract
---

# A Person's Pull Request Feedback Is Recorded On The Clarification Log And Supersedes The Issue

## Context

A person's pull request comments reached only the revision run that applied them (`RevisionTrigger.ReviewFeedback`); the issue kept only the feedback cutoff. The next review judged the change against the issue text, so a change a person asked for that departed from the issue came back as blocking unmet criteria, and the findings fix was told to undo it or stopped to ask. A run resumed after a question lost the feedback the same way.

## Decision

When Foundry consumes a person's review feedback, the transition into `RevisionQueued` appends a `Requested` round to the issue's clarification log: the comments, the omitted count and the newest comment's time. It is never open and asks nothing.

- **The log is the one channel.** Review, implement and re-triage read `Requested` rounds through the log they already receive. The `ReviewFeedback` trigger keeps its comments as the revision run's own instruction.
- **The newest person input wins.** A round with person input (an answered `Asked` or a `Requested` round) supersedes the issue text and earlier rounds where they conflict. The log section states this once; review judges each criterion as the log leaves it and cites the round.
- **Shedding protects the newest person input.** A round's comments count as its input; older rounds are cut first.

## Considered Options

- **Keep the comments only on the trigger and add a review fact:** rejected, a second channel to wire into prompts and the dashboard.
- **Read the pull request thread live at review dispatch:** rejected, the findings fix still never sees it, and Foundry's own comments and resolved threads need filtering.
- **The issue text wins and conflicts are surfaced:** rejected, it moves the round trip to review instead of removing it.

## Consequences

- Code older than this decision cannot read a `Requested` round; recovery from a bad deploy is roll-forward.
- Feedback consumed before the decision is not backfilled.
