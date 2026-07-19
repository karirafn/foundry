# Return Affected Repositories Synchronously from the Account-Update Response

## Context

When a credential token is rotated, repos that lose namespace coverage become ineligible. The operator needs to know which repos were affected without polling or a separate query.

## Decision

The `PUT /api/accounts/{id}` response envelope changes from a bare `CredentialSummary` to `CredentialUpdateResult(CredentialSummary Credential, IReadOnlyList<AffectedRepository> AffectedRepositories)`. The affected list is computed synchronously inside the request — `CredentialRotationService` re-evaluates all repos in the before∪after namespace union (bounded-concurrent, bound 4) and returns only those whose eligibility status changed.

## Considered Options

- **SignalR push after save** — decouples the response from re-evaluation latency, but requires the client to subscribe and correlate an event to the originating request. Adds complexity for an operation that already waits for token validation.
- **Separate `/recheck` endpoint** — keeps update simple but forces a two-round-trip workflow and leaves a window where stale status is visible.

## Consequences

- The `PUT` response is slightly slower for credentials covering many repositories (bounded to 4 concurrent re-evaluations).
- Callers must update to read `.credential` instead of the bare summary for credential fields.
- The Angular client (step 7) must be updated to use `CredentialUpdateResult` and expose `.affectedRepositories`.
