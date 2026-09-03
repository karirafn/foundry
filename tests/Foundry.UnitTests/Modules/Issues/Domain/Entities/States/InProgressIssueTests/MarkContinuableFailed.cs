using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.InProgressIssueTests;

public sealed class MarkContinuableFailed
{
    [Fact]
    public void WhenMarkedContinuableFailed_ReturnsContinuableFailedIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();

        // Act
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            "foundry/1/add-feature",
            "Container exited with code 1",
            FailureCategory.NonZeroExit,
            DateTimeOffset.UtcNow);

        // Assert
        failed.Id.ShouldBe(inProgress.Id);
    }

    [Fact]
    public void WhenMarkedContinuableFailed_RaisesIssueContinuableFailedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();

        // Act
        inProgress.MarkContinuableFailed(
            "foundry/1/add-feature",
            "Container exited with code 1",
            FailureCategory.NonZeroExit,
            DateTimeOffset.UtcNow);

        // Assert
        IssueContinuableFailed domainEvent = inProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuableFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(inProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedContinuableFailed_ContinuableFailedIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();
        string branchName = "foundry/1/add-feature";
        string failureReason = "Container exited with code 1";
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            branchName,
            failureReason,
            FailureCategory.NonZeroExit,
            failedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => failed.BranchName.ShouldBe(branchName),
            () => failed.FailureReason.ShouldBe(failureReason),
            () => failed.FailedAt.ShouldBe(failedAt),
            () => failed.PullRequestUrl.ShouldBe(string.Empty),
            () => failed.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => failed.IssueNumber.ShouldBe(inProgress.IssueNumber),
            () => failed.Title.ShouldBe(inProgress.Title),
            () => failed.DetectedAt.ShouldBe(inProgress.DetectedAt));
    }
}
