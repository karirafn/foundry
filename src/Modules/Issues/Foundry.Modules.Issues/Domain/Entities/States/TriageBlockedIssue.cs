using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// An issue that hasn't been triaged and has open blockers (ADR 0102), or one triage found blocked. It waits
/// untriaged, so triage judges it against the code its blockers wrote; when they close it moves to
/// <see cref="TriageQueuedIssue"/>, and when the tracker refuses to link a blocker triage named it moves to
/// <see cref="TriageFailedIssue"/>. Unlike a
/// <see cref="BlockedIssue"/>, which has been triaged or skipped triage and moves to <see cref="FreshQueuedIssue"/>.
/// It is not a <see cref="QueuedIssue"/>: it is never a dispatch candidate and holds no worker slot.
/// </summary>
public sealed class TriageBlockedIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private TriageBlockedIssue()
    {
    }

    private TriageBlockedIssue(IssueId id) : base(id)
    {
    }

    internal static TriageBlockedIssue Untriaged(Issue source, IReadOnlyList<int> blockers)
    {
        if (blockers.Count == 0)
        {
            throw new InvalidOperationException("Cannot block an issue without specifying at least one blocker.");
        }

        TriageBlockedIssue blocked = new(source.Id);
        blocked.CopySharedPropertiesFrom(source);
        blocked.SetBlockedBy(blockers);
        return blocked;
    }

    internal static TriageBlockedIssue FromTriageInProgress(TriageInProgressIssue source, TriageVerdict.Blocked verdict)
    {
        TriageBlockedIssue blocked = new(source.Id);
        blocked.CopySharedPropertiesFrom(source);
        blocked.RecordTriageVerdict(verdict);
        blocked.SetBlockedBy(NamedThenRecorded(verdict, source.BlockedBy));
        return blocked;
    }

    private static List<int> NamedThenRecorded(TriageVerdict.Blocked verdict, IReadOnlyList<int> recorded)
    {
        List<int> named = [.. verdict.BlockedBy.Select((TriageBlocker blocker) => blocker.IssueNumber)];
        return [.. named, .. recorded.Except(named)];
    }

    public TriageQueuedIssue Unblock()
    {
        TriageQueuedIssue queued = TriageQueuedIssue.FromTriageBlocked(this);
        AddDomainEvent(new Events.IssueTriageQueued(Id, MonitoredRepositoryId));
        return queued;
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
        string failureReason = $"The tracker rejected linking #{blockerIssueNumber} as a blocker: {rejection}";
        TriageFailedIssue failed = TriageFailedIssue.FromTriageBlocked(
            this,
            workerRunId,
            failureReason,
            FailureCategory.BlockerLinkFailed,
            failedAt);
        AddDomainEvent(new Events.IssueTriageFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
