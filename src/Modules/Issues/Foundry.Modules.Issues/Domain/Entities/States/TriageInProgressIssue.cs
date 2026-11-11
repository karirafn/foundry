using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

namespace Foundry.Modules.Issues.Domain.Entities.States;

/// <summary>
/// An issue a triage worker is reading (ADR 0097). It holds a worker slot like any running worker, and leaves by its
/// run's verdict: <c>ready</c> through the dependency check, <c>needs_input</c> to awaiting input, or a failed run to
/// <see cref="TriageFailedIssue"/>.
/// </summary>
public sealed class TriageInProgressIssue : Issue
{
    // Private parameterless constructor for EF Core materialization.
    private TriageInProgressIssue()
    {
    }

    private TriageInProgressIssue(IssueId id) : base(id)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    internal static TriageInProgressIssue FromTriageQueued(TriageQueuedIssue source, WorkerRunId workerRunId)
    {
        TriageInProgressIssue inProgress = new(source.Id);
        inProgress.CopySharedPropertiesFrom(source);
        inProgress.SetBlockedBy(source.BlockedBy);
        inProgress.WorkerRunId = workerRunId;
        return inProgress;
    }

    /// <summary>
    /// Triage judged the issue ready: it records the verdict and goes through the dependency check, queued when it has
    /// no open blockers and blocked otherwise.
    /// </summary>
    public Issue MarkReady(TriageVerdict.Ready verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        return ReleaseThroughDependencyCheck(verdict);
    }

    /// <summary>Triage needs answers first: it records the verdict and waits for input, then triages again.</summary>
    public AwaitingInputIssue MarkNeedsInput(TriageVerdict.NeedsInput verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);

        AwaitingInputIssue awaiting = AwaitingInputIssue.FromTriageInProgress(this, verdict);
        AddDomainEvent(new Events.IssueAwaitingInput(Id, MonitoredRepositoryId, IssueNumber));
        return awaiting;
    }

    public TriageFailedIssue MarkFailed(string failureReason, FailureCategory failureCategory, DateTimeOffset failedAt)
    {
        TriageFailedIssue failed = TriageFailedIssue.FromTriageInProgress(this, failureReason, failureCategory, failedAt);
        AddDomainEvent(new Events.IssueTriageFailed(Id, MonitoredRepositoryId));
        return failed;
    }
}
