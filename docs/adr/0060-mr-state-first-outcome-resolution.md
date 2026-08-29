# MR State-First Outcome Resolution with a Pure Resolver

## Context

After a worker container exits, Foundry must determine what happened and transition the issue accordingly. The prior approach queried `HasBranchCommitsAsync` first, then (if commits present and exit 0) polled for an open PR. This ordering failed to detect already-merged PRs: a merged PR would cause `HasBranchCommits` to return false (branch deleted on merge) and the exit-0+no-commits path would incorrectly mark the issue as Unchanged instead of Completed.

A pure, independently-testable decision function was needed to replace the four private service methods (`ProcessExitedRunAsync`, `ProcessSuccessWithCommitsAsync`, `ProcessSuccessWithoutCommitsAsync`, `ProcessNonZeroExitAsync`) that interleaved provider queries with DB writes and event dispatch.

## Decision

Query the merge request by branch name first (MR-state-first), using the new `GetMergeRequestByBranchAsync` that returns `MergeRequestPresence { None, Open, Merged, Closed }`. Fall back to exit-code + branch-commits logic only when no MR exists. Encapsulate this decision matrix in a dedicated, side-effect-free `WorkerOutcomeResolver` class that takes provider queries and an output parser, performs no DB writes or container ops, and returns a sealed `WorkerOutcome` discriminated union. The service applies the returned outcome (transitions, events, container removal) in a single `ApplyOutcomeAsync` method.

`BranchName` survives remote branch deletion (stored at run start), making it a reliable key for the MR query even after the branch is removed by the merge.

## Considered Options

**Targeted 404-patch vs MR-state-first:** An alternative was to patch only the merged-branch-deleted case (treat 404 on `HasBranchCommitsAsync` as possibly-merged and re-query the MR). This would preserve the existing flow for the common case but leave the ordering inverted — exit code and commits still dominate, with MR state as a late correction. MR-state-first is a cleaner invariant: the provider's merge record is the ground truth regardless of branch presence or exit code.

**Pure resolver class vs private methods:** The existing logic lived in private methods on `WorkerDispatchService` (~1070 lines). Private methods cannot be independently unit-tested without the full service infrastructure (DbContext, orchestrator, scope factory). A separate class makes the decision matrix exhaustively testable with only a scripted `IPostExitProviderQueries` stub — no database, no container ops. The resolver class also brings the service under the ~300-line guideline when wiring is completed in stage 6.

**`ErrorKind` init-property vs `Error` subtype:** The `NotFound` discriminator on `Error` (needed to distinguish "branch does not exist" from "provider unreachable") could have been an `Error` subtype (`NotFoundError : Error`). The init-property approach (`public ErrorKind Kind { get; init; }`) keeps `Error` as a flat `sealed record`, avoids adding a hierarchy that callers must pattern-match, and lets the resolver check `error.Kind == ErrorKind.NotFound` with a single property read.

## Consequences

- Merged PRs are detected regardless of whether the branch was deleted (the MR object persists on the provider after deletion).
- The resolver decision matrix is covered by an exhaustive unit test per row, independently of the service infrastructure.
- `WorkerDispatchService` is split into a resolver (pure logic) and an apply method (side effects), matching the service-extraction pattern in the DDD skill.
- Stage 6 (wiring `ApplyOutcomeAsync`) can delete the four `Process*` methods once the resolver is wired; this ADR covers only the resolver introduction (stage 5).
