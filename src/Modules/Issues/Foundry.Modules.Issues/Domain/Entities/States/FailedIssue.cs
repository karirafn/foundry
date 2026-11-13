using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Workers.Contracts;

using FailureCategoryVO = Foundry.Modules.Workers.Contracts.FailureCategory;

namespace Foundry.Modules.Issues.Domain.Entities.States;

public sealed class FailedIssue : Issue, IAutoRetryableFailedState
{
    public override IssueStage Stage => IssueStage.Implement;

    public override bool NeedsAction => true;

    public override bool AwaitsDispatch => false;

    public override bool WaitsOnBlockers => false;

    public override IssueCodeStep? CodeStep => null;

    // Private parameterless constructor for EF Core materialization.
    private FailedIssue()
    {
    }

    private FailedIssue(IssueId id, DateTimeOffset enteredStateAt) : base(id, enteredStateAt)
    {
    }

    public WorkerRunId WorkerRunId { get; private set; }

    public string FailureReason { get; private set; } = string.Empty;

    public FailureCategoryVO FailureCategory { get; private set; } = FailureCategoryVO.NonZeroExit;

    public DateTimeOffset FailedAt { get; private set; }

    internal static FailedIssue FromInProgress(
        InProgressIssue source,
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
    {
        FailedIssue failed = new(source.Id, failedAt);
        failed.CopySharedPropertiesFrom(source);
        failed.WorkerRunId = source.WorkerRunId;
        failed.FailureReason = failureReason;
        failed.FailureCategory = failureCategory;
        failed.FailedAt = failedAt;
        return failed;
    }

    internal static FailedIssue FromReview(
        ReviewIssue source,
        string failureReason,
        FailureCategoryVO failureCategory,
        DateTimeOffset failedAt)
    {
        FailedIssue failed = new(source.Id, failedAt);
        failed.CopySharedPropertiesFrom(source);
        failed.WorkerRunId = source.WorkerRunId;
        failed.FailureReason = failureReason;
        failed.FailureCategory = failureCategory;
        failed.FailedAt = failedAt;
        return failed;
    }

    public FreshQueuedIssue Retry(DateTimeOffset transitionedAt)
    {
        FreshQueuedIssue queued = FreshQueuedIssue.FromRetry(this, transitionedAt);
        AddDomainEvent(new Events.IssueQueued(Id, MonitoredRepositoryId));
        return queued;
    }
}
