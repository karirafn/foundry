using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// An issue that hasn't been triaged and has open blockers (ADR 0102), or one triage found blocked. It waits
/// untriaged, so triage judges it against the code its blockers wrote; when they close it moves to
/// <see cref="TriageQueuedIssue"/>, and when the tracker refuses to link a blocker triage named it moves to
/// <see cref="TriageFailedIssue"/>.
/// A <see cref="BlockedIssue"/> differs: it has been triaged or skipped triage, and moves to
/// <see cref="FreshQueuedIssue"/> when its blockers close.
/// It is not a <see cref="QueuedIssue"/>: it is never a dispatch candidate and holds no worker slot.
/// </summary>
public sealed class TriageBlockedIssue : Issue
{
    public override IssueStage Stage => IssueStage.Triage;

    public override bool NeedsAction => false;

    public override bool AwaitsDispatch => true;

    public override bool WaitsOnBlockers => true;

    public override IssueCodeStep? CodeStep => null;

    /// <summary>The longest failure reason the failed issue stores; the provider's text is cut to fit.</summary>
    public const int FailureReasonMaxLength = 500;

    private const string Ellipsis = "…";

    // Private parameterless constructor for EF Core materialization.
    private TriageBlockedIssue()
    {
    }

    private TriageBlockedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    /// <summary>
    /// The triage run whose verdict named the blockers; <c>null</c> when the issue waits untriaged on blockers found
    /// before any triage run.
    /// </summary>
    public WorkerRunId? WorkerRunId { get; private set; }

    internal static TriageBlockedIssue Untriaged(Issue source, IReadOnlyList<int> blockers, DateTimeOffset transitionedAt)
    {
        if (blockers.Count == 0)
        {
            throw new InvalidOperationException("Cannot block an issue without specifying at least one blocker.");
        }

        TriageBlockedIssue blocked = new(source.Id, transitionedAt);
        blocked.CopySharedPropertiesFrom(source);
        blocked.SetBlockedBy(blockers);
        return blocked;
    }

    internal static TriageBlockedIssue FromTriageInProgress(
        TriageInProgressIssue source,
        TriageVerdict.Blocked verdict,
        DateTimeOffset transitionedAt)
    {
        TriageBlockedIssue blocked = new(source.Id, transitionedAt);
        blocked.CopySharedPropertiesFrom(source);
        blocked.RecordTriageVerdict(verdict);
        blocked.WorkerRunId = source.WorkerRunId;
        blocked.SetBlockedBy(NamedThenRecorded(verdict, source.BlockedBy));
        return blocked;
    }

    private static List<int> NamedThenRecorded(TriageVerdict.Blocked verdict, IReadOnlyList<int> recorded)
    {
        List<int> named = [.. verdict.BlockedBy.Select((TriageBlocker blocker) => blocker.IssueNumber)];
        return [.. named, .. recorded.Except(named)];
    }

    public TriageQueuedIssue Unblock(DateTimeOffset transitionedAt)
    {
        TriageQueuedIssue queued = TriageQueuedIssue.FromTriageBlocked(this, transitionedAt);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return queued;
    }

    /// <summary>
    /// How a refused link relates to this state: whether it is the one the triage run behind this state's verdict
    /// wrote. A refusal reported by an earlier or later run, or for an issue that waits on blockers found before
    /// triage, describes a state this issue no longer holds.
    /// </summary>
    public LinkRefusalMatch MatchRefusedLink(WorkerRunId workerRunId, int blockerIssueNumber)
    {
        if (WorkerRunId != workerRunId || TriageVerdict is not TriageVerdict.Blocked verdict)
        {
            return LinkRefusalMatch.FromAnotherRun;
        }

        return verdict.ReasonFor(blockerIssueNumber) is null
            ? LinkRefusalMatch.BlockerNotNamed
            : LinkRefusalMatch.Matches;
    }

    /// <summary>
    /// The tracker refused to link a blocker triage named: triage fails, so the person sees why and retries or skips
    /// triage. The verdict and the blockers stay on the failed issue.
    /// </summary>
    public TriageFailedIssue FailBlockerLink(
        WorkerRunId workerRunId,
        int blockerIssueNumber,
        string rejection,
        DateTimeOffset failedAt)
    {
        string failureReason = SingleLineWithinLimit(
            $"The tracker rejected linking #{blockerIssueNumber} as a blocker: {rejection}");
        TriageFailedIssue failed = TriageFailedIssue.FromTriageBlocked(
            this,
            workerRunId,
            failureReason,
            FailureCategory.BlockerLinkFailed,
            failedAt);
        AddDomainEvent(new Events.IssueTriageFailed(Id, MonitoredRepositoryId));
        return failed;
    }

    private static string SingleLineWithinLimit(string text)
    {
        string flattened = text.Trim().ReplaceLineEndings(" ");

        if (flattened.Length <= FailureReasonMaxLength)
        {
            return flattened;
        }

        int keep = FailureReasonMaxLength - Ellipsis.Length;

        if (char.IsHighSurrogate(flattened[keep - 1]))
        {
            keep--;
        }

        return string.Concat(flattened.AsSpan(0, keep), Ellipsis);
    }
}
