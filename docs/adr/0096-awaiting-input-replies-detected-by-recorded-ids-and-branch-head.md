# Awaiting Input: Replies Detected by Recorded Comment IDs and Branch Head

## Context

A worker run sometimes needs a person to answer a question before the work can continue, for example an unclear requirement, or a required change under a path workers are denied such as `.github/workflows/**`. Before this decision every "Needs attention" state meant that something finished or broke, and the run either guessed or failed. Under [ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md) the agent may only return a verdict; code has to own posting the question, recognising the answer, and resuming the work.

## Decision

A run whose verdict is `needs_input` moves the issue to `AwaitingInputIssue` (`awaiting_input`, display group "Needs attention"). Code posts the questions to the provider, waits for input, and resumes the work.

- **Way in**: the implement role returns `{outcome, questions}` through `claude --json-schema`, and the final stream-json `result` event carries it as `structured_output`. The verdict applies only when the run exits 0. A merged pull request still completes the issue, and a non-zero exit still goes through failure classification. A missing or invalid verdict means the agent did not ask: the outcome comes from the provider facts alone, and the run records `verdict: missing|invalid`. Nothing becomes `needs_input` without a schema-valid verdict.
- **Where the question goes**: on the pull request when one exists, otherwise on the issue. Foundry reads replies only in that thread.
- **Post first, then wait**: the state is entered first, and a poll step posts the question with backoff. The post status is the closed union `Pending | Posted(commentId, postedAt) | Failed(reason, nextAttemptAt)`. Reply detection starts only once the question is `Posted`, and the cutoff is the posted comment's `created_at`.
- **What counts as input**: after the cutoff, any of the following counts as input. All of them go through the existing 2-minute quiet period.
  - a new eligible comment whose provider ID is not in Foundry's posted-comment registry;
  - a head SHA on the run's branch that differs from the one recorded at entry;
  - while the question lives on the issue thread, an issue-body hash that differs from the one recorded at entry.
- **Own comments are excluded by ID, never by author**: Foundry posts with the account PAT, and on a single-operator install the PAT owner is also the person answering. Every reader, including the review-feedback reader, skips IDs in the posted-comment registry.
- **Clarification log on the `Issue` aggregate**: each round (questions, what arrived, timestamps, or "continued without answer") is appended to a log carried with the shared issue properties. Every prompt for a later run renders the log, so an answer lasts as long as the issue does, not just for one run.
- **Resume target recorded at entry**: code derives a `ResumeTarget` from the asking role and the provider facts at exit: `Revision(branch, pr)` when the run left an open pull request, `Continuation(branch)` when it left commits but no pull request, and `Fresh` when it pushed nothing. Other roles add their own variants.
- **Ways out**: input arrives; the issue is closed, or its pull request is closed without merging; or an operator chooses **Continue without answer**. One reminder is posted after a delay set in Global Settings. A run started by **Continue without answer** has a `needs_input` verdict refused, and its outcome comes from the facts.

## Considered Options

- **Exclude Foundry's comments by author (PAT owner)**: rejected because on a single-operator install it also drops every human reply, so the state could only be left through the reminder or an operator action.
- **Exclude by a hidden marker in the comment body**: rejected because a human who quotes Foundry's comment carries the marker along, and their reply is silently dropped.
- **Carry the answer on the next run's trigger**: rejected because the answer would reach only one run, and every queued state would need its own field.
- **Post the question inside the run-completion handler**: rejected because outbox redelivery would post it twice.
- **Fail the run when the verdict is missing**: rejected because it throws away a finished pull request over a missing JSON footer, and contradicts the provider facts.

## Consequences

- Nothing in Foundry writes to a branch while its issue is in `awaiting_input`, which is what makes a head change mean "a human pushed". If a Foundry write to waiting branches is ever added (for example, extending the [ADR 0087](0087-foundry-rebases-worker-branches-through-the-provider-after-each-run.md) rebase to idle states), it would set off this trigger itself.
- Posting comments is a new provider write capability on GitHub and GitLab, made with the account PAT.
- The implement role gains a verdict schema, so `verdict.schema.json` is no longer limited to judgment roles.
