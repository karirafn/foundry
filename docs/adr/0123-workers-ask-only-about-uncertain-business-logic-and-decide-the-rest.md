---
reaches: module-contract
---

# Workers Ask Only About Uncertain Business Logic And Decide The Rest

## Context

Triage asked a person a question for nearly every contradiction or gap it found, and 25 of 26 recorded rounds were answered with a bare `ok`: the recommendation was accepted unchanged. Each round parks the issue in awaiting input and spends a poll-and-quiet-period cycle. [ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md) keeps transitions in code and has Claude return a schema-validated verdict, and [ADR 0097](0097-every-new-issue-is-triaged-before-dispatch.md) reuses awaiting input for "not ready".

## Decision

Triage, implement and review ask a person only about **uncertain business logic**, meaning what the system does, shows, sends or records for its users or domain, when no ADR, domain document, current behaviour or the issue's stated intent settles it. They also ask about an **important implementation detail**: one that reaches beyond one class or feature slice (structure, a contract between modules, an external contract or wire value, security, performance, availability or data retention, a dependency, an invariant other code relies on) and whose reversal after merge takes meaningful rework. They decide everything else.

- **Decisions are verdict data.** A triage verdict's optional `decisions` array holds, per decision, the question, the answer itself (not an instruction to edit the issue), a closed `ground` (`settled_business` | `implementation_detail`) and at least one `file:line` evidence item. A missing or invalid decision fails the run as `verdict_invalid`, never a silent decision.
- **The clarification log is the one channel.** A round is `Asked` (questions, optionally decisions) or `Decided` (decisions only, never open). Implement, review and re-triage read decisions through the log they already receive.
- **Each role decides what it finds.** The implementer records details it decides in the pull request `notes`/`attention`, and overturns a triage decision only when running code proves it wrong, saying so in `attention`.
- **Review judges each criterion as the log leaves it** and raises a decision that should have been asked as an advisory finding.
- **The veto is visible and exists.** Foundry posts each triage decision on the issue thread without waiting; **Re-triage** (queued or blocked) revisits them, and pull request review covers a run already started.

## Considered Options

- **Ask everything triage is unsure of**: the status quo; rejected for the 25-of-26 `ok` rate.
- **Hold decided issues in a grace state before queueing**: rejected, a new state and timer for a veto the data shows is never used.
- **A per-repository autonomy setting**: rejected for the second lifecycle to test, as in ADR 0097.
- **Write accepted answers into the issue body**: rejected, a provider write to a person's text that the body-hash watch reads as an edit.

## Consequences

- Triage classifies its own decisions; the schema forces a ground and evidence but cannot prove the classification. The thread comment, the dashboard and the review advisory are the checks.
- An implement run can be claimed before the decision comment posts; pull request review is then the checkpoint.
