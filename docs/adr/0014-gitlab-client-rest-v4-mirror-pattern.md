# GitLab Client: REST v4 Mirror Pattern

## Context

Adding GitLab as a second provider required a new HTTP client. The existing `GitHubHttpClient` uses hand-rolled REST calls with `HttpClient` rather than an SDK or GraphQL. The GitLab client needed to cover the same operations: issue listing, dependency resolution, merge request status, review feedback, branch operations, and repository listing.

## Decision

Mirror the `GitHubHttpClient` structure in `GitLabHttpClient` — same public method signatures, same `Result<T>` return types, same error handling patterns. Use GitLab REST v4 instead of GraphQL.

This produces two clients that are structurally identical and easy to compare side-by-side, at the cost of some duplication. Review feedback uses unresolved discussion threads (tier-independent) rather than "Request changes" reviewer state (Premium-only).

## Considered Options

- **GraphQL** — rejected: REST v4 covers every needed endpoint and mirrors the existing GitHub client's hand-rolled approach.
- **"Request changes" reviewer state** for feedback — rejected: Premium-only feature. Unresolved threads are tier-independent and serve the same purpose.
