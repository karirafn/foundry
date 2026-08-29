# Dispatch Context Union and Typed Dispatch Seam

## Context

The dispatch seam — the `ClaimedIssueDispatch` payload carried through the `IssueClaimed` outbox event — previously used three weakly-typed fields:

- `ProviderType: string` on `RepositoryDispatchInfo`, converted to `WorkerProvider` at each call site via `WorkerProvider.FromDiscriminator`.
- `BranchName: string` on `ClaimedIssueDispatch`, re-parsed at each consumer.
- `RevisionContext? Revision` and `ContinuationContext? Continuation` as a nullable pair, making it representable (and indistinguishable from code) to have both set simultaneously or neither set.

The string discriminator on `RepositoryDispatchInfo` was justified in [ADR 0016](0016-typed-worker-provider.md) with "EF cannot instantiate abstract records inside `Select`".
That rationale does not apply: `RepositoryDispatchQueries.GetDispatchInfoAsync` materialises its result with `FirstOrDefaultAsync` and constructs the `RepositoryDispatchInfo` record by hand — there is no `Select`.

The nullable pair for dispatch context means the handler must defensively check both fields, and adding a third context kind (e.g., an amendment run) requires yet another nullable property and yet another guard.

## Decision

Type all three concepts directly at the seam:

- **`WorkerProvider Provider`** on `RepositoryDispatchInfo` — `WorkerProvider.GitHub` or `WorkerProvider.GitLab` (sealed record hierarchy from ADR 0016), set in `RepositoryDispatchQueries` by switching on the credential CLR type. `WorkerProvider.FromDiscriminator` and `WorkerProviderErrors` are deleted.
- **`BranchName BranchName`** on `ClaimedIssueDispatch` — the `BranchName` value object from `Foundry.Shared`, eliminating all re-parse sites.
- **`DispatchContext Context`** on `ClaimedIssueDispatch` — a new required sealed union replacing the nullable pair:

```csharp
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Fresh),        "fresh")]
[JsonDerivedType(typeof(Revision),     "revision")]
[JsonDerivedType(typeof(Continuation), "continuation")]
public abstract record DispatchContext
{
    private DispatchContext() { }
    public sealed record Fresh(string BranchName) : DispatchContext;
    public sealed record Revision(string BranchName, string PullRequestUrl,
        IReadOnlyList<ReviewComment> Comments) : DispatchContext;
    public sealed record Continuation(string BranchName, string? FailureReason = null)
        : DispatchContext;
}
```

`SystemPromptBuilder.Build` switches exhaustively over the union.
A discard arm throws `UnreachableException` — the intended failure mode for a future credential subtype added without a matching `DispatchContext` variant; the compiler flags the missing case.

`DispatchContext` carries `[JsonPolymorphic]` / `[JsonDerivedType]` so the sealed union survives the outbox round-trip.
The wire shape of persisted `IssueClaimed` outbox rows changes: the `Context` field replaces the `Revision`/`Continuation` nullable pair.
There are no external consumers of the outbox; in-flight rows should be drained before deploy.

## Consequences

A dispatch carrying both a revision and a continuation context is now unrepresentable — that class of bug is eliminated at compile time.

`WorkerCapacityAvailableHandler` loses three `FromDiscriminator` guard blocks; `IssueClaimedHandler` loses the re-parse of `BranchName`.

Adding a third context kind requires one new `DispatchContext` variant; the compiler then flags all unhandled switch arms.

This ADR **supersedes the `RepositoryDispatchInfo`-carries-a-string clause of ADR 0016**.
ADR 0016's core decision — represent provider identity as a closed record hierarchy rather than an enum or runtime host-sniffing — still stands and is unchanged.
