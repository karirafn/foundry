using System.Text.Json.Serialization;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ReviewFeedback), "review_feedback")]
[JsonDerivedType(typeof(FailedChecks), "failed_checks")]
[JsonDerivedType(typeof(RebaseConflict), "rebase_conflict")]
[JsonDerivedType(typeof(InputProvided), "input_provided")]
public abstract record RevisionTrigger
{
    private RevisionTrigger()
    {
    }

    public abstract string Discriminator { get; }

    public sealed record ReviewFeedback(
        IReadOnlyList<ReviewComment> Comments,
        int OmittedCommentCount,
        DateTimeOffset? NewestConsumedCommentAt) : RevisionTrigger
    {
        public override string Discriminator => "review_feedback";
    }

    public sealed record FailedChecks(IReadOnlyList<FailingCheck> FailingChecks) : RevisionTrigger
    {
        public override string Discriminator => "failed_checks";
    }

    /// <summary>
    /// The post-run rebase of the worker branch onto the default branch conflicted (ADR 0087): the worker merges
    /// the default branch into the branch and resolves the conflicts in a merge commit. Payload-free — the worker
    /// discovers the conflicting paths from <c>git merge</c>.
    /// </summary>
    public sealed record RebaseConflict : RevisionTrigger
    {
        public override string Discriminator => "rebase_conflict";
    }

    /// <summary>
    /// A person answered the questions an earlier run asked (ADR 0096). Payload-free: the answer lives on the issue's
    /// clarification log, which every implement prompt renders.
    /// </summary>
    public sealed record InputProvided : RevisionTrigger
    {
        public override string Discriminator => "input_provided";
    }
}
