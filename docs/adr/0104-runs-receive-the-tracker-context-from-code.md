# Runs Receive the Tracker Context from Code

## Context

Workers never read the tracker ([ADR 0100](0100-workers-hold-no-provider-credential-and-foundry-publishes.md)); the issue's own content is read by code at dispatch and rendered into the prompt ([ADR 0098](0098-judgment-roles-run-restricted-with-code-supplied-issue-content.md)).
Without the issues an issue references, triage runs asked people whether another issue had merged or whether to wait: 6 of 27 triaged issues at discussion time.

## Decision

At dispatch, Monitoring reads the issue's Tracker Context and Foundry renders it into every role's prompt after the issue content, as XML-encoded untrusted data.

- **Referenced Issues** — the issue's same-project links (relation `blocked_by`, `blocks` or `relates_to`), then its `#N` mentions in first-mention order, deduplicated, without itself, at most 10. Each carries title, state, labels, the state of its closing merge request (GitLab `closed_by`, GitHub `closedByPullRequestsReferences`; never merge requests that only mention it) and its body.
- **Open-Issue Index** — the number and title of up to 100 open issues, newest first, without the issue itself.
- **A tracker read never blocks the dispatch** — a failed link, issue or index read is left out with a one-line note, and the run starts. Only the issue's own content read fails a dispatch.
- **Shed first** — when the prompt exceeds its budget, Referenced Issue bodies (last-selected first) and then the index are shed before any of the issue's own content, revision payload or clarification input. Attributes and titles are never shed.
- **Both roles, same context** — triage judges readiness on the inputs the implement run will have.

## Considered Options

- **Fail the dispatch on a failed referenced read**: rejected because a deleted or private referenced issue would hold the issue forever with no automatic exit.
- **Triage only**: rejected so triage never judges an issue ready on context the implement run lacks.
- **Only the index, or only the Referenced Issues**: rejected; the index finds sibling precedent the issue does not mention, and the references carry the state and closing merge request a title cannot.

## Consequences

- Each dispatch makes up to 23 more provider calls, recorded in the provider rate budget ([ADR 0066](0066-provider-api-request-budget-governor.md)).
- Triage's `blocked` verdict ([ADR 0103](0103-triage-defers-on-open-dependencies-by-linking-blockers.md)) names only open issues from this context.
