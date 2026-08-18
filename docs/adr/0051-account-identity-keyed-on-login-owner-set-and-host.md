# Account identity keyed on login, derived owner set, and host

## Context

The account form disabled Save whenever a second account matched an existing one on `(name, baseUrl)`.
An account's display name *is* its PAT Owner, and multiple accounts may legitimately share one PAT Owner — GitHub fine-grained PATs are each bound to a single resource owner, so reaching two owners requires two tokens and therefore two accounts.
Foundry's own token requirements instruct operators to create exactly that shape, so the form told them to do it and then blocked it: rotating the token on either of two accounts sharing a provider login was impossible.

The check also lived client-side, where it could not compute the token's owner set without a full repository listing.
It was therefore keyed on the one attribute the client did have — the login — which `DOMAIN.md` already documented as insufficient to identify an account.

## Decision

An account is identified by `(PAT Owner, derived owner-namespace set, host)`, and duplicate detection moves server-side into the create and update handlers.

A request is a duplicate when another credential on the same host carries the same `Credential.Name` *and* its Namespace Claims intersect the incoming token's derived owner namespaces.
It is rejected with 409 naming the colliding account and the shared owner.
Same login with disjoint owners is permitted; a different login reaching an already-claimed owner stays a Namespace Claim conflict resolved through takeover.

On a token-bearing update, namespace derivation runs first and both guards evaluate before the credential is mutated.
`CredentialRotationService.RotateAsync` no longer derives internally — it receives the derived set from the caller — so `INamespaceDeriver` leaves its constructor.
A derivation returning `Unavailable` rejects the update and persists nothing.

## Considered Options

**Keep the check client-side, extending `ValidateToken` to return the derived namespaces.**
Rejected: it puts a full paginated repository listing behind every token blur, and it leaves a second copy of the identity predicate in TypeScript.
That drift is what produced this defect — the client's notion of identity diverged from the domain's, and nothing at the boundary caught it.

**Compare owner sets by equality instead of intersection.**
Rejected: a partially overlapping token — a classic PAT reaching both owners — would match neither account and would fall through to the namespace-filtering path, which drops every claimed owner and can leave the account serving nothing.

**Mutate the credential first, then guard, inside a transaction with rollback.**
Rejected: an `OutboxSaveChangesInterceptor` drains the outbox on `SaveChanges` within the same request, so a rejected token sitting on the tracked entity is one flush away from being persisted.
Guarding before the mutation removes the hazard structurally rather than relying on rollback discipline.

**Hoist the resource owner onto `Credential` as a unique column.**
Rejected: a token's owner set can be multi-valued, which is why it is already modelled as `credential_namespaces` rows.

## Consequences

Rejecting on `Unavailable` blocks a legitimate rotation whenever the provider listing is failing; the operator retries.
This is chosen over a guard that holds while the provider is healthy and silently opens when it is not.
The no-token update path and the background repository-recheck path keep their retain-prior-namespaces behaviour.

`RotateAsync` loses its `Unavailable` branch as unreachable — the caller now rejects that outcome first — so retain-prior on transient failure survives only in `RecheckRepositoryEligibility.RefreshNamespacesAsync`.

Deleting the client-side check moves all duplicate feedback post-submit, costing one round trip.
During resolution the operator sees a valid "Authenticated as X" result and learns of the duplicate only on Save.

The unique `(host, namespace)` constraint on `credential_namespaces` is unchanged.
This decision adds no invariant; it surfaces the existing one as a specific 409 ahead of the coarser conflict/takeover flow, which previously offered to transfer a namespace away from the operator's own identical account.

The guard is deliberately qualified on the login, which leaves one case open: a token for a *different* login whose reachable owners are all already claimed still routes into the namespace-filtering path and can strand the account on zero claims.
That defect is tracked separately as issue #439.
