using Foundry.Shared;

namespace Foundry.Modules.Monitoring.Contracts;

/// <summary>
/// Raised by Monitoring's merge step when Foundry's merge of a merge-pending issue's pull request resolved
/// (ADR 0105). <see cref="Outcome"/> is one of <c>merged</c>, <c>disabled</c>, <c>skipped</c> or
/// <see cref="FeedbackOutcome"/>; <see cref="Reason"/> is set for <c>skipped</c> (see <see cref="MergeSkipReasons"/>).
/// For <see cref="FeedbackOutcome"/> the merge was never attempted: a person's review feedback arrived first, and
/// <see cref="Comments"/>, <see cref="OmittedCommentCount"/> and <see cref="NewestCommentAt"/> carry it as
/// <see cref="PullRequestChangesRequested"/> does (issue #917).
/// </summary>
public sealed record MergeResolved(
    MonitoredRepositoryId RepositoryId,
    int IssueNumber,
    string Outcome,
    string? Reason = null,
    IReadOnlyList<ReviewComment>? Comments = null,
    int OmittedCommentCount = 0,
    DateTimeOffset? NewestCommentAt = null) : IIntegrationEvent
{
    /// <summary>A person's review feedback stopped the merge; the issue revises instead.</summary>
    public const string FeedbackOutcome = "feedback";
}
