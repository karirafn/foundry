using System.Text.Json.Serialization;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ReviewFeedback), "review_feedback")]
[JsonDerivedType(typeof(FailedChecks), "failed_checks")]
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
}
