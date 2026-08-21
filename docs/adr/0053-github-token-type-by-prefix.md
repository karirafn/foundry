# GitHub token type classified by prefix at validation

## Context

GitHub's `/user` endpoint omits the `X-OAuth-Scopes` response header for fine-grained PATs.
Before this fix, `ValidateTokenAsync` returned `ScopesUnverifiable` whenever that header was absent, without distinguishing the token type.
Fine-grained PATs (`github_pat_` prefix) are Foundry's *recommended* token type — the token requirements UI links directly to the fine-grained PAT creation page — so `ScopesUnverifiable` fired on every successful validation of the recommended token.
The warning was therefore always present for the happy path, not for the exceptional cases it was designed to flag.

## Decision

Classify a GitHub token's type by its prefix at validation time.
A token starting with `github_pat_` is a fine-grained PAT; when `X-OAuth-Scopes` is absent, return `Authenticated` with empty `MissingScopes` — no caution, no advisory.
Any other GitHub token served without `X-OAuth-Scopes` (classic PATs on GHES, server configurations that suppress the header) still returns `ScopesUnverifiable`.

Probe-gating as an alternative was rejected: the write probe (`IGitHubWriteProber`) requires a repository slug and runs only after namespace derivation inside `CreateAccount`, so it cannot gate the validation-only path that `UpdateAccount` and the standalone validate-token endpoint exercise.

## Consequences

`ScopesUnverifiable` is now meaningful: it covers GitLab group/project access tokens where `personal_access_tokens/self` returns non-2xx, and GitHub tokens without `X-OAuth-Scopes` that are not fine-grained PATs.
Fine-grained permission enforcement is delegated to the provider and the create-time write probe.

The classification depends on GitHub's documented, host-independent `github_pat_` prefix convention.
A future GitHub token type with a different prefix falls back to the safe `ScopesUnverifiable` path — no caution is suppressed for unknown prefixes.

The fix is entirely server-side; all downstream consumers (`CreateAccount.Handler`, `UpdateAccount.Handler`, the Angular form, and the setup step) are unchanged because they already treat `Authenticated`/empty `MissingScopes` as the success path.
