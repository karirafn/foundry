# Retain Credential Namespaces on Transient Provider Failure During Rotate

## Context

When a credential token is rotated via `PUT /api/accounts/{id}`, the handler re-derives the namespace set by listing the new token's writable repositories. If that listing call fails transiently (network error, provider 5xx), the previous implementation returned an empty listing and called `SetNamespaces([])`, which wiped all namespace associations — dropping coverage for every repository the credential previously covered.

## Decision

When the namespace deriver returns `Unavailable` (transient failure) during a token rotation, the `CredentialRotationService` keeps the prior namespace set unchanged and marks all currently-resolving repositories `Unreachable` (recheck) rather than dropping their coverage. The operator is informed via the affected-repositories list in the response.

## Consequences

- Transient provider failures at rotate time no longer cause silent coverage loss.
- A credential that was rotated during a provider outage retains its previous coverage until the next successful derivation (e.g., triggered by `RecheckRepositoryEligibility`).
- The `NamespaceDerivationOutcome` discriminated union (`Derived` / `Unavailable`) makes the distinction explicit at the type level, preventing accidental conflation of "empty listing" with "transient failure".
