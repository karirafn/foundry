# Triage Defers on Open Dependencies by Linking Blockers

> **2026-11-12 — Consequence:** a poll can race the blocker link. `TriageInProgressIssue.MarkBlocked` records `BlockedBy` locally, while Monitoring writes the tracker link later through the outbox. Every poll reads each issue's dependencies from the tracker (`RepositoryPoller.DetectDependenciesAsync`) and `IssueDependencyApplier.ApplyAsync` overwrites `BlockedBy` with what it read. A poll whose read precedes the link write and whose apply follows `MarkBlocked` therefore sets `BlockedBy` to `[]`, and a `TriageBlockedIssue` with no blockers `Unblock()`s back to `TriageQueuedIssue`. The cost is one extra triage run: the re-run links whatever blockers it names, and a further extra run needs another poll to land inside that run's link-write window, so repeats are unlikely and each costs one run. This was present from the start (an omission in this ADR, not drift) and is accepted.

## Context

[ADR 0102](0102-triage-runs-once-blockers-clear.md) holds an issue untriaged until its linked blockers close, so triage judges code that exists. An issue can also depend on an open issue it only mentions (`builds on #27`) or does not mention at all. Code cannot tell a dependency from a passing mention, and triage runs asked a person instead: 6 of 27 triaged issues asked whether another issue had merged or whether to wait, including about issues already completed. Code decides waiting and ordering ([ADR 0094](0094-code-decides-claude-judges-only-what-code-cannot.md)); triage supplies only the judgment that a dependency exists. This extends the triage flow of [ADR 0097](0097-every-new-issue-is-triaged-before-dispatch.md) with a third route.

## Decision

Triage returns `outcome: "blocked"` with `blockedBy: [{ issue, reason }]` when the work needs an open issue first, and Foundry turns that judgment into tracker blocker links.

- **Allowed blockers** — each `issue` is an open issue Foundry supplied at dispatch: a Referenced Issue with `state="open"` or an Open-Issue Index entry. The list holds at least one entry. Any other number makes the verdict `verdict_invalid`, which goes to `TriageFailedIssue` like every invalid verdict.
- **No cycles from code** — before enqueueing writes, code checks each entry against the stored dependency edges. An entry that would close a cycle sends the issue to `TriageFailedIssue` with `blocker_cycle`, naming the cycle, and nothing is linked. A cycle signals confused scope between issues, which a person resolves.
- **State first, link second** — the verdict moves `TriageInProgress` to `TriageBlockedIssue` with the named `BlockedBy`, and enqueues the link write through the outbox. Monitoring writes each link on the provider (GitHub `dependencies/blocked_by`, GitLab `is_blocked_by`) and counts an existing link as success.
- **The tracker stays the only blocker source** — the dependency pass keeps reading provider links. A pass that runs before the write lands sends the issue to `TriageQueued`, and the claim-time blocker re-check from ADR 0102 returns it to `TriageBlockedIssue` once the link reads back.
- **Write failures exit** — a permanent provider rejection (a 4xx other than 429) raises `BlockerLinkFailed`, moving `TriageBlockedIssue` to `TriageFailedIssue` with `blocker_link_failed`, where Retry triage and Skip triage apply. A transient failure that exhausts the relay's retries leaves the link unwritten, so the next dependency pass re-queues triage, which judges again.
- **People see why** — code posts one comment listing each linked issue with its reason and stating that removing a link releases the issue. Its ID goes in the posted-comment registry, so it never counts as a reply.
- **Triage only** — the implement role has no `blocked` outcome. It reaches an issue with open dependencies only through Skip triage or Continue without answer, both deliberate operator overrides.

## Considered Options

- **Record triage-found blockers only in Foundry**: rejected because the dependency pass replaces `BlockedBy` from provider links on every poll, so a second blocker source needs its own column and merge rule, and a person can neither see nor remove it in the tracker.
- **Treat an open mention as context and ask when it matters**: rejected because it keeps the "should this wait?" question that code can answer by holding the issue.
- **Write the links before changing state**: rejected because a transient write failure that exhausts its retries would leave the issue in `TriageInProgress` with no exit.
- **Leave a code-written cycle to today's cycle handling**: rejected because both issues would wait on a person with no automatic exit.
- **A `ready` verdict carrying dependencies**: rejected because it routes through `BlockedIssue` to `FreshQueued`, skipping the re-triage ADR 0102 requires once the code lands.

## Consequences

- Foundry now writes issue links to the tracker. The account token needs issue write access on GitHub, and on GitLab the right to update both issues; Foundry already posts issue comments under the same access.
- A wrong dependency costs a wait until that issue closes, or until a person removes the link the comment names.
- A transient failure that exhausts the relay's retries costs one extra triage run before the link is written again.
