# 0055. Distinguish Unreachable Eligibility Reason (NeverProbed / RateLimited / BranchRulesUnavailable)

## Context

A GitHub write probe returns 403 both for a genuine missing push permission and for
rate-limit exhaustion (primary via `X-RateLimit-Remaining: 0`, secondary via `Retry-After`).
`ClassifyProbeResponse` mapped every 403 to `Missing`, so a spent REST budget marked every
repository `Ineligible` with a false `cannot-push` violation and halted dispatch. Separately,
`RepositoryEligibility.Unreachable` carried no reason, so the UI showed a single
branch-protection message that was wrong for both the rate-limit and never-probed cases — an
`Unknown` verdict short-circuits to `Unreachable` before any branch-rules GET is issued. [ADR 0054](0054-split-eligibility-cadence.md)
established the split-cadence eligibility model and the `Unknown` self-heal cooldown; this decision
refines the failure taxonomy on top of it.

## Decision

Classification keys on explicit rate-limit headers: a 403 with `X-RateLimit-Remaining: 0` or a
present `Retry-After` becomes `Result.Fail(GitHubErrors.RateLimitExhausted)`; a 403 with headroom
or no rate-limit headers stays `Missing` (fail-closed for genuine permission denial).

The three `Unreachable` causes are made explicit with an `UnreachableReason`
(`NeverProbed`, `RateLimited`, `BranchRulesUnavailable`). Because the composer receives only the
persisted `WriteProbeVerdict` and cannot otherwise tell a rate-limit indeterminacy from a
transport one, `WriteProbeVerdict.Unknown` carries an `UnknownReason` (`Transport`, `RateLimited`).
The composer derives the `UnreachableReason` from the verdict reason and stamps
`BranchRulesUnavailable` at the branch-rules-GET failure site. The reason is surfaced through the
`RepositoryEligibilityInfo` contract and rendered as a cause-specific message. Rate-limit and
never-probed `Unreachable` states remain on the existing `Unknown` self-heal path — no new
liveness exit is introduced.

## Consequences

- A rate-limited repository is `Unreachable` (self-healing), never `Ineligible` — dispatch resumes
  automatically when the budget resets, and the operator sees a rate-limit message, not a false
  permission error.
- `WriteProbeVerdict.Unknown` and `RepositoryEligibility.Unreachable` gain defaulted fields;
  legacy JSON deserializes to `Transport`/`NeverProbed`, so no migration is needed (both persist as
  TEXT).
- The `RepositoryEligibilityInfo` contract change regenerates `openapi/v1.json` and `schema.ts`.
- A future non-403 rate-limit signal (e.g. 429) would need the same header inspection; the
  `IsRateLimited` helper is the single place to extend.
