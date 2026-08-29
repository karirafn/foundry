# 0053. Reject fully-claimed token rotations instead of offering takeover

**Date:** 2026-08-21
**Status:** Accepted

## Context
Rotating a credential's token to one that authenticates as a different login, whose entire derived owner set is already claimed by other credentials, stranded the account on zero namespace claims (issue #439). The create path resolves the analogous conflict with a takeover panel that transfers claims from their current holders. The open question was whether the rotation path should reuse that takeover flow or reject.

## Decision
The rotation (update-account) path rejects with a structured 409 (`NamespaceClaimedElsewhereResponse`) naming the claimed owners and their holders. Takeover remains exclusive to the create-account path. Partial-overlap rotations continue to succeed (retained set non-empty), unchanged from #438; only the fully-claimed different-login case is rejected. A routine token refresh must not be able to strip a colleague's account of its namespace claims, and a fully-claimed rotation is never a legitimate narrowing — narrowing retains coverage by definition.

## Consequences
Easier: the rotation form is safe by construction — the easy path cannot evict a colleague. The rejection path performs zero writes, so no transaction or rollback logic is added. Harder: an operator who genuinely intends to move ownership must do so from the create/add-account surface, not the rotation form. A distinct response DTO (`NamespaceClaimedElsewhereResponse`) is required so the frontend can render a read-only rejection without triggering the create path's takeover panel — the two 409 shapes must not be unified.
