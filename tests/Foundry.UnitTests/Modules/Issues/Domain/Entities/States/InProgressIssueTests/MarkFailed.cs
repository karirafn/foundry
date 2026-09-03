using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.InProgressIssueTests;

public sealed class MarkFailed
{
    [Fact]
    public void WhenMarkedFailed_ReturnsFailedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        FailedIssue failed = inProgress.MarkFailed("Container exited with code 1", failedAt, FailureCategory.NonZeroExit);

        // Assert
        failed.Id.ShouldBe(inProgress.Id);
    }

    [Fact]
    public void WhenMarkedFailed_RaisesIssueFailedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        inProgress.MarkFailed("Container exited with code 1", failedAt, FailureCategory.NonZeroExit);

        // Assert
        IssueFailed domainEvent = inProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(inProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedFailed_FailedIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();
        string failureReason = "Container exited with code 1";
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        FailedIssue failed = inProgress.MarkFailed(failureReason, failedAt, FailureCategory.NonZeroExit);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => failed.FailureReason.ShouldBe(failureReason),
            () => failed.FailedAt.ShouldBe(failedAt),
            () => failed.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => failed.IssueNumber.ShouldBe(inProgress.IssueNumber),
            () => failed.Title.ShouldBe(inProgress.Title),
            () => failed.DetectedAt.ShouldBe(inProgress.DetectedAt));
    }

    [Fact]
    public void WhenMarkedFailed_FailedIssueHasFailureCategory()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        FailedIssue failed = inProgress.MarkFailed("Usage limit reached", failedAt, FailureCategory.UsageLimited);

        // Assert
        failed.FailureCategory.ShouldBe(FailureCategory.UsageLimited);
    }
}
