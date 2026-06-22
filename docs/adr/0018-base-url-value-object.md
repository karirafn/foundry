# BaseUrl Value Object

## Context

`Account.BaseUrl` was typed as `Uri`, with inline scheme checks duplicated across `GitHubAccount`, `GitLabAccount` (both `Create` and `Update`), and two command validators. None of these checks rejected userinfo-bearing URLs (e.g. `https://attacker@github.com`), which could flow unmodified into worker dispatch as a clone URL.

## Decision

Extract a `BaseUrl` sealed record value object that owns the absolute/https/no-userinfo invariant as the single validated construction path. `BaseUrl.Create(string)` returns `Result<BaseUrl>` and is the only way to produce a valid value. The entity and validators accept `BaseUrl`, making the invalid state unrepresentable.

The EF read boundary uses `BaseUrl.FromPersistedString(string)`, which routes through `Create` and throws `InvalidOperationException` on failure rather than providing a bypass factory. A second construction path that skips validation would be easy to misuse and could silently admit the invariant violation through the persistence layer.

## Considered Options

- **Inline userinfo check in each validator** — rejected: leaves the parse/validate logic duplicated and the entity still accepting any `Uri`, so invalid states remain representable.
- **`internal FromPersistence` bypass factory for EF** — rejected: a second path that skips validation is easy to misuse; routing through `Create` keeps a single validated path.
- **Single `BaseUrl.Invalid` error code for all failures** — rejected: a distinct `BaseUrl.ContainsCredentials` lets tests assert precisely against the userinfo-rejection rule.

## Consequences

- Legacy persisted rows containing userinfo (e.g. stored before the guard existed) will throw `InvalidOperationException` on EF materialization. Accepted for phase-1 POC with disposable data — no migration.
- `ValidateToken.cs` now also rejects userinfo in its inline URL parse, tightening that endpoint as an accepted side effect with no external contract change.
