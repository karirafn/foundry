using System.Text.Json.Serialization;

using Foundry.Modules.Monitoring.Contracts;

namespace Foundry.Modules.Issues.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Fresh), "fresh")]
[JsonDerivedType(typeof(Revision), "revision")]
[JsonDerivedType(typeof(Continuation), "continuation")]
public abstract record DispatchContext
{
    private DispatchContext()
    {
    }

    public sealed record Fresh(string BranchName) : DispatchContext;

    public sealed record Revision(
        string BranchName,
        string PullRequestUrl,
        IReadOnlyList<ReviewComment> Comments) : DispatchContext;

    public sealed record Continuation(string BranchName, string? FailureReason = null) : DispatchContext;
}
