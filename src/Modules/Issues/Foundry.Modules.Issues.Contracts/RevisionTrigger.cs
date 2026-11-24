using System.Text.Json.Serialization;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

namespace Foundry.Modules.Issues.Contracts;

// The $type tokens and member names are persisted in the revision_trigger column and carried in
// outbox events; renaming one needs a data migration of the stored JSON.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(ReviewFeedback), "review_feedback")]
[JsonDerivedType(typeof(ReviewFindings), "review_findings")]
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

    /// <summary>
    /// The reviewer requested changes (ADR 0113): the worker addresses the review's findings on the existing branch.
    /// Unlike <see cref="ReviewFeedback"/>, no person drove it, so auto-merge survives it.
    /// </summary>
    /// <param name="Findings">The review's findings, blocking first then advisory, each in the verdict's order.</param>
    /// <param name="UnmetCriteria">The acceptance criteria the reviewer judged unmet, with its evidence.</param>
    /// <param name="TestsAssessment">The reviewer's judgment of the change's tests.</param>
    /// <param name="TestsReason">The reviewer's reason for <paramref name="TestsAssessment"/>.</param>
    public sealed record ReviewFindings(
        IReadOnlyList<ReviewFinding> Findings,
        IReadOnlyList<ReviewUnmetCriterion> UnmetCriteria,
        ReviewTestsAssessment TestsAssessment,
        string TestsReason) : RevisionTrigger
    {
        public override string Discriminator => "review_findings";
    }

    /// <param name="FailingChecks">The failing checks the fix run addresses.</param>
    /// <param name="HeadSha">The head commit the red verdict was read on (ADR 0106), or <see langword="null"/> when
    /// it was unknown or the trigger was stored before the head was recorded.</param>
    public sealed record FailedChecks(IReadOnlyList<FailingCheck> FailingChecks, string? HeadSha = null) : RevisionTrigger
    {
        public override string Discriminator => "failed_checks";
    }

    /// <summary>
    /// The post-run rebase of the worker branch onto the default branch conflicted (ADR 0087): the worker rebases the
    /// branch onto the default branch and resolves the conflicts at each stop (ADR 0101). Payload-free — the worker
    /// discovers the conflicting paths from <c>git rebase</c>.
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
