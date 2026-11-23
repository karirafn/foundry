---
reaches: module-contract
---

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
`Duplicate`, and `Failure` subtypes) rather than `Result<CredentialCreationResult>`. The `Result<T>` type in
this codebase carries a flat `Error(Code, Message)` record that cannot carry structured payloads
without string-encoding. A custom union keeps the payload typed and avoids encoding. This follows
the `WorkerRunLogResult` precedent.

**D3 — Atomic takeover via `IDbContextTransaction`.** The delete-then-insert within a transaction
removes the holder's `CredentialNamespace` rows for the listed namespaces before inserting the new
credential, sidestepping the unique index `ix_credential_namespaces_host_value`. SQLite's
single-writer serialization means a concurrent request cannot interleave. The handler resolves
"meanwhile-vanished" conflicts (listed in `takeoverNamespaces` but no longer claimed) by proceeding
normally — no conflict to transfer means the namespace is simply claimed.

**D4 — Never-steal semantics for rotation; takeover only at create time.** `CredentialRotationService`
and `RecheckRepositoryEligibility` call `credential.SetNamespaces(derived, claimedByOthers)` — the
overload that silently subtracts namespaces claimed by other credentials rather than conflicting.
Background recheck cannot prompt the user, so silent subtraction avoids blocking the rotation.
Explicit takeover is only available at create time.

The rotation (update-account) path never offers takeover. A token rotation to a different login
whose *entire* non-empty derived owner set is already claimed by other credentials would strand the
account on zero namespace claims, so it is rejected before any write with a structured 409 —
`UpdateAccountConflictResponse` with reason `ClaimedElsewhere` — naming the claimed owners and their
holders. A partial-overlap rotation still succeeds: subtraction leaves a non-empty retained set, and
narrowing retains coverage by definition, so only the fully-claimed case is rejected. A routine
token refresh therefore cannot strip a colleague's account of its namespace claims.

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
- **Offer takeover on a fully-claimed rotation** (reuse the create path's takeover panel from the
  rotation form) — rejected: a routine token refresh must not be able to evict a colleague, and a
  fully-claimed rotation is never a legitimate narrowing.

## Consequences

- The rotation form is safe by construction — the easy path cannot evict a colleague. The
  fully-claimed rejection performs zero writes, so it needs no transaction or rollback logic.
- An operator who genuinely intends to move ownership must do so from the create/add-account
  surface, not the rotation form.
- The create and update 409 shapes stay distinct — `CreateAccountConflictResponse` carries the
  takeover candidates, `UpdateAccountConflictResponse` does not — so the frontend renders a
  read-only rejection on rotation without triggering the create path's takeover panel. The two must
  not be unified ([ADR 0056](0056-one-response-schema-per-endpoint-status.md)).

## Revisions
- 2026-08-29 — Rotation rejects a fully-claimed different-login token with a structured 409 rather than offering takeover; folded in the former 0065 record (#439).
