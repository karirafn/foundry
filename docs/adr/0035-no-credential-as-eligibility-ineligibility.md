# No Covering Credential as an Eligibility Ineligibility Reason

## Context

After scoping credential resolution by repository namespace, a monitored repository whose owner does not match any credential's namespace cannot be served by a provider. The system needed to decide how to surface this state.

## Decision

"No covering credential" is modelled as an `Ineligible` eligibility reason (a new `EligibilityViolation` factory: `NoCredential(topLevelNamespace)`, rule key `no-credential:<namespace>`) rather than a dispatch error or a new eligibility variant.

The `RepositoryEligibilityEvaluator` now resolves the covering credential first (via `ICredentialResolver`). When none covers the repository, it sets eligibility to `Ineligible` with the `no-credential` violation and skips the branch-protection probe entirely — no credential means no provider client, so the probe cannot run. When a credential is found, the evaluator builds the provider from that credential's token and proceeds with the existing branch-protection check.

The top-level namespace reported in the violation key is the last element of `Namespace.PrefixesOf(slug)` (the broadest prefix), since `PrefixesOf` returns longest-first. This is the namespace a credential would need to claim to cover the repository.

## Considered Options

**New eligibility variant** (`NoCredential`) — rejected because the frontend and downstream code already handle arbitrary `Ineligible` violations by rule/description. Adding a variant would require touching the discriminated union, all switch expressions over it, and the JSON discriminator.

**Dispatch error** — rejected because eligibility is evaluated at add/recheck time and on every poll cycle, not only at dispatch. Surfacing it as an eligibility reason keeps it visible in the dashboard without requiring a dispatch attempt.
