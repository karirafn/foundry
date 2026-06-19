# Repository Identity: Nested-Namespace Path with Host-Scoped Uniqueness

## Context

`RepositorySlug` modelled a repository as exactly two segments (`owner/name`), validated by a hard `parts.Length != 2` check and persisted as a single string with a global unique index. This fit GitHub, whose paths are always `owner/repo`. Adding GitLab breaks both assumptions: GitLab projects live under nested groups, so `path_with_namespace` can have arbitrarily many segments (`group/subgroup/project`), and the same path string (`acme/web`) can refer to entirely different repositories across providers or self-hosted instances.

## Decision

Repository identity is the full namespace path plus the host it lives on.

- `RepositorySlug` accepts two or more `/`-separated segments. Each segment is validated by the existing per-segment character regex; `Name` is the last segment and `Owner` is everything before it, so GitHub (always single-segment owner) keeps building `repos/{Owner}/{Name}` unchanged while GitLab consumes the URL-encoded full path. The path remains a natural identifier — no numeric project-ID surrogate.
- Uniqueness is scoped to `(Host, Slug)` rather than the bare slug. `MonitoredRepository` carries a `Host` column denormalized from the account's base URL at creation. Two accounts pointing at the same host and path still collide (preserving the no-duplicate-monitoring guarantee), while the same path on different hosts is allowed.

## Considered Options

- **Numeric GitLab project IDs** — rejected: introduces a surrogate key, loses the human-readable natural identifier, and forces a mixed identity model across providers.
- **Per-account uniqueness** — rejected: discards the deliberate guarantee that the same real repository cannot be monitored twice, since two accounts can share a host.

## Consequences

- Requires an EF Core migration: add the `host` column and replace the bare-slug unique index with a composite `(host, slug)` index.
- `RepositorySlug.Owner` now means "namespace path" and may contain `/` for GitLab; code treating it as a single segment is GitHub-specific by contract.
