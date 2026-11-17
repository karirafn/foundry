using System.Text.Json.Serialization;

namespace Foundry.Modules.Issues.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(Fresh), "fresh")]
[JsonDerivedType(typeof(Revision), "revision")]
[JsonDerivedType(typeof(Continuation), "continuation")]
[JsonDerivedType(typeof(Triage), "triage")]
public abstract record DispatchContext
{
    private DispatchContext()
    {
    }

    /// <summary>The worker role the run is dispatched under; derived from the queued state's context variant.</summary>
    public abstract DispatchRole Role { get; }

    /// <summary>
    /// Whether the run resumes a clarification round an operator closed with Continue without answer (ADR 0096). Such a
    /// run may not send the issue straight back to awaiting input: its <c>needs_input</c> verdict is refused.
    /// </summary>
    public bool ContinuedWithoutAnswer { get; init; }

    public sealed record Fresh(string BranchName) : DispatchContext
    {
        public override DispatchRole Role => DispatchRole.Implement;
    }

    public sealed record Revision(
        string BranchName,
        string PullRequestUrl,
        RevisionTrigger Trigger) : DispatchContext
    {
        public override DispatchRole Role => DispatchRole.Implement;
    }

    public sealed record Continuation(string BranchName, string? FailureReason = null) : DispatchContext
    {
        public override DispatchRole Role => DispatchRole.Implement;
    }

    /// <summary>A triage run (ADR 0097): it judges the issue and returns a verdict, and gets no branch.</summary>
    public sealed record Triage : DispatchContext
    {
        public override DispatchRole Role => DispatchRole.Triage;
    }
}
