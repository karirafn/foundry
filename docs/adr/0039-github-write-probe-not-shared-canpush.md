# GitHub Write Permission Probe as a Separate Method, Not a Change to the Shared CanPush Path

## Context

Foundry needs to verify that a GitHub token has write access to repository contents, issues,
and pull requests before accepting a credential or marking a repository eligible.
The existing `GetPushPermissionAsync` reads the `permissions.push` field from the repository
metadata response and is shared with the GitLab code path.

## Decision

Introduce `ProbeContentsWriteAsync` (and later `ProbeIssuesWriteAsync`, `ProbePullRequestsWriteAsync`)
as new methods on `GitHubHttpClient` rather than modifying or extending `GetPushPermissionAsync`.

`GetPushPermissionAsync` is a shared probe surface used by both GitHub and GitLab credential paths.
GitLab has no partial write-permission mode — a token either has push access or it does not — so
adding GitHub-specific probe logic there would conflate two different permission models.
The `permissions.push` field is also a read-only declaration from GitHub rather than a live
write test, so it cannot classify missing Contents vs. Issues vs. Pull Requests permissions.

## Consequences

- GitHub and GitLab write-permission checks remain independently evolvable.
- `GetPushPermissionAsync` is unchanged and continues to serve both providers.
- `ProbeContentsWriteAsync` encapsulates the invalid-payload trick (`POST git/refs` with
  all-zeros SHA) and its `422`/`403`/`404` classification logic in isolation.
