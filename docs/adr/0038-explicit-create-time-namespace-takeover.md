# Explicit Create-Time Namespace Takeover

## Context

When a user adds a credential whose derived namespaces are already claimed by an existing credential
on the same host, the system must surface the conflict rather than silently overwriting or silently
skipping. The conflict may be intentional (the user is rotating ownership to a new token) or
unintentional (two tokens sharing an org prefix). The user needs control over which path is taken.

## Decision

**D1 — Fail-first, explicit takeover.** `POST /api/accounts` without `takeoverNamespaces` returns
409 with a structured body listing each conflicting namespace, the holder credential ID, and the
holder name. The caller must retry with `takeoverNamespaces` to override the conflict — there is no
implicit overwrite.

**D2 — Custom discriminated union for handler outcome.** `CreateAccount.Handler` returns
`CreateAccount.Outcome` (an abstract class with nested `Created`, `Conflict`, `InvalidTakeover`,
and `Failure` subtypes) rather than `Result<CredentialCreationResult>`. The `Result<T>` type in
this codebase carries a flat `Error(Code, Message)` record that cannot carry structured payloads
without string-encoding. A custom union keeps the payload typed and avoids encoding. This follows
the `WorkerRunLogResult` precedent.

**D3 — Atomic takeover via `IDbContextTransaction`.** The delete-then-insert within a transaction
removes the holder's `CredentialNamespace` rows for the listed namespaces before inserting the new
credential, sidestepping the unique index `ix_credential_namespaces_host_value`. SQLite's
single-writer serialization means a concurrent request cannot interleave. The handler resolves
"meanwhile-vanished" conflicts (listed in `takeoverNamespaces` but no longer claimed) by proceeding
normally — no conflict to transfer means the namespace is simply claimed.

**D4 — Never-steal semantics for background rotation.** `CredentialRotationService` and
`RecheckRepositoryEligibility` call `credential.SetNamespaces(derived, claimedByOthers)` — the
overload that silently subtracts namespaces claimed by other credentials rather than conflicting.
Background recheck cannot prompt the user, so silent subtraction avoids blocking the rotation.
Explicit takeover is only available at create time.

**D5 — Synchronous repo recheck after takeover.** After a committed takeover, the handler calls
`RepositoryEligibilityDiffer.FindResolvingReposAsync` for the new credential's namespaces and
returns `affectedRepositories` in the `CredentialCreationResult`. Repos are rechecked on the
request thread to give the caller immediate feedback. The differ is the same component used by the
rotation service, extracted to avoid duplication.

## Considered Options

- **Silent overwrite** — rejected: hides accidental credential collisions and destroys the holder's
  coverage silently.
- **Error union on `Result<T>`** — rejected: requires string-encoding structured payloads or
  extending `Error`, which is a shared type not owned by this feature.
- **Async background recheck** — rejected: caller receives no confirmation that takeover had any
  effect; synchronous recheck keeps the response self-describing.
