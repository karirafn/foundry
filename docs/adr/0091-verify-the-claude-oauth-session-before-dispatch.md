# Verify the Claude OAuth Session Before Dispatch

## Context

Foundry used to learn that the Claude OAuth session had expired only after a dispatched worker failed on it. The worker's output was classified as an auth failure, `WorkerAuthenticationFailed` invalidated the account, and dispatch paused. As a result, the first sign of an expired login was a burst of failed runs. If classification missed the error, the operator was never told.

Foundry has to check the session itself, without spending the refresh token in a way that strands the copy workers use. The CLI rotates the refresh token on every refresh ([ADR 0090](0090-workers-share-only-the-oauth-credential-store.md)). A refresh that Foundry posted on its own would leave the token on the shared credential volume dead, unless Foundry also took over the CLI's store lock and file format.

The CLI 2.1.316 was checked against the credential file on 2026-11-05:

- `.credentials.json` holds `claudeAiOauth.{accessToken, refreshToken, expiresAt (epoch ms), refreshTokenExpiresAt, scopes, subscriptionType, …}`.
- When the access token has expired, `claude auth status --json` attempts a refresh. It takes the shared `.storage-write` lock and writes the rotated token back.
- When the token endpoint answers `invalid_grant`, the CLI marks the token dead on disk: `refreshToken: ""`, `accessToken: ""`, `expiresAt: 0`. `auth status` still prints `loggedIn: true` and exits 0, so its stdout and exit code cannot be used.
- When the refresh hits a network error, the file is left untouched: the token is still expired and the refresh token is still present.

## Decision

A periodic `OAuthSessionCheckService` runs once at startup and then every 5 minutes. It drives a single-flight `OAuthSessionCheckCoordinator`. The check runs only for a valid OAuth account with a committed login, and only while no login session is active.

`CredentialsOrchestrator.CheckOAuthSessionAsync` starts a credential-helper container and reads `.credentials.json`, which `OAuthCredentialFile` classifies. If the access token has expired while a refresh token is still stored, the CLI attempts the refresh (`timeout 20 claude auth status --json`) and the file is classified again. Foundry never posts the refresh token itself. Every refresh goes through the CLI, under the same lock workers use, and the rotated token lands on the shared volume.

| Status | Meaning | Effect |
|---|---|---|
| `Active` | Unexpired, or no expiry | none |
| `Expired` | Expired, refresh token still present (refresh failed transiently) | none — retried next tick |
| `Unreadable` | File unparseable | none — retried next tick |
| `RefreshRejected` | CLI dead-token marker (empty refresh token) | invalidate |
| `NotRefreshable` | Expired with no refresh token | invalidate |
| `SignedOut` | File or `claudeAiOauth` missing | invalidate |

Invalidation reuses the existing path. `ClaudeAccount.Invalidate(reason)` and `CredentialsInvalidated` are enqueued through the outbox in one transaction, so dispatch pauses (`CredentialGate`) and the dashboard shows the sign-in-again prompt. A Docker failure invalidates nothing. The account is also not invalidated if it changed while the helper container ran (its `UpdatedAt` moved) or if a login started during the check. After the operator signs in again, the existing `CredentialsValidated` flow restores dispatch.

## Considered Options

- **Have Foundry call the token endpoint itself**: rejected. The rotated refresh token would have to be written back with the CLI's lock and file format. Any mismatch would strand the workers' copy.
- **Classify the `auth status` output**: rejected. It reports `loggedIn: true` even after the CLI has cleared a rejected token.
- **Check at claim time**: rejected. It would add a helper-container round-trip to every claim. A periodic check bounds the window to one interval, and workers that hit a dead token in that window are still caught by the reactive `WorkerAuthenticationFailed` path.

## Consequences

- The dead-token marker and the refresh-on-`auth status` behaviour are undocumented CLI internals. Re-verify them, together with the `CLAUDE_SECURESTORAGE_CONFIG_DIR` check from ADR 0090, before a CLI bump is merged.
- The check starts a short-lived helper container every 5 minutes for a signed-in OAuth account. It reaches the network only when the access token has already expired.
- A refresh rejected by a worker or by the usage poll is also detected: the dead-token marker is read straight from the file, with no further refresh attempt.
