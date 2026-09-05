using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ContinuationQueuedIssueTests;

public sealed class FromContinuableFailed
{
    [Fact]
    public void WhenCreatedFromContinuableFailed_CopiesSharedPropertiesAndBranchName()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue failed = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).ContinuableFailed();

        // Act
        ContinuationQueuedIssue queued = ContinuationQueuedIssue.FromContinuableFailed(failed);

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(failed.Id),
            () => queued.MonitoredRepositoryId.ShouldBe(failed.MonitoredRepositoryId),
            () => queued.IssueNumber.ShouldBe(failed.IssueNumber),
            () => queued.Title.ShouldBe(failed.Title),
            () => queued.Author.ShouldBe(failed.Author),
            () => queued.DetectedAt.ShouldBe(failed.DetectedAt),
            () => queued.BranchName.ShouldBe(failed.BranchName));
    }

    [Fact]
    public void WhenCreatedFromContinuableFailed_CopiesFailureReason()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue failed = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).ContinuableFailed();

        // Act
        ContinuationQueuedIssue queued = ContinuationQueuedIssue.FromContinuableFailed(failed);

        // Assert
        queued.FailureReason.ShouldBe(failed.FailureReason);
    }

    [Fact]
    public void WhenSourceFailureReasonExceedsMaxLength_TruncatesToMaxLength()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        string longReason = new('x', 501);
        ContinuableFailedIssue failedWithLongReason = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithFailureReason(longReason)
            .ContinuableFailed();

        // Act
        ContinuationQueuedIssue queued = ContinuationQueuedIssue.FromContinuableFailed(failedWithLongReason);

        // Assert
        queued.FailureReason.Length.ShouldBe(500);
    }
}
