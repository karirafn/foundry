# Any Human Comment on a PR Is Actionable Review Feedback

## Context

Foundry reacted to a narrow slice of PR feedback: GitHub `reviews(states:[CHANGES_REQUESTED])`
only, GitLab unresolved *resolvable* discussions only.
Plain conversation comments were ignored, and the actionability rule lived twice in infrastructure
(`GitHubHttpClient`, `GitLabHttpClient`).

## Decision

Every human-authored, non-system comment created on the PR after `FeedbackCutoffAt` is actionable.
The rule moves out of both HTTP clients into one provider-agnostic `ActionableFeedbackPolicy`.

- **Surfaces.** GitHub: a single GraphQL request returns `pullRequest.comments`,
  `pullRequest.reviewThreads` (comments of unresolved threads), and `pullRequest.reviews { body submittedAt }`
  — no state filter, inline comments de-duped. GitLab: every note of every discussion, `per_page=100`
  with bounded pagination.
- **Author policy.** Bots out (`author.__typename == "Bot"` on GitHub), system notes out
  (`system: true` on GitLab), self in — a comment authored by the credential's own login is
  actionable, so the policy never filters on login. GitLab exposes no bot flag on discussion-note
  authors, so GitLab bot exclusion rests on `system` plus the worker self-comment prompt instruction.
- **`createdAt` semantics.** The cutoff compares `createdAt`, never `updatedAt` — a comment created
  before the cutoff but edited after it stays excluded.
- **Quiet period.** Comments newer than `now - 2 minutes` are held and consumed on a later poll,
  preventing a revision from firing mid-conversation. Hardcoded 2-minute constant on the policy.
- **Cap.** The newest 50 by `createdAt` are kept; the omitted count is rendered in the worker prompt.
- **Clients map only.** `GitHubHttpClient` and `GitLabHttpClient` project provider JSON to
  `ProviderComment`; filtering, ordering, capping, and the quiet period live in the policy.

## Considered Options

- **Keep `CHANGES_REQUESTED`-only (GitHub) / resolvable-only (GitLab)** — rejected: ignores plain
  conversation comments, the common review channel.
- **Username-based GitLab bot filter** — rejected: the Discussions API exposes no bot field on note
  authors; a username heuristic is fragile and out of scope.
- **`GlobalSettings` tunable for the quiet period** — rejected: a 2-minute constant is sufficient now;
  promotion is a one-line change.
- **Advance `FeedbackCutoffAt` to the newest consumed comment now** — deferred to a follow-up;
  `NewestCommentAt` is plumbed on the event and `RevisionQueuedIssue` but not yet consumed.

## Consequences

One place owns the actionability rule; the clients shrink to mappers.
The worker can still comment on its own PR (mitigated only by prompt).
GitLab bot comments other than system notes are not filtered.
