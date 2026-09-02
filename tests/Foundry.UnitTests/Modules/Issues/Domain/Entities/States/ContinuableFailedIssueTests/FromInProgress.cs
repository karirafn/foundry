using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ContinuableFailedIssueTests;

public sealed class FromInProgress
{
    [Fact]
    public void WhenCreatedFromInProgress_CopiesSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();

        // Act
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            "foundry/1/add-feature",
            "Container exited with code 1",
            "generic_failure",
            DateTimeOffset.UtcNow);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.Id.ShouldBe(inProgress.Id),
            () => failed.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => failed.IssueNumber.ShouldBe(inProgress.IssueNumber),
            () => failed.Title.ShouldBe(inProgress.Title),
            () => failed.Body.ShouldBe(inProgress.Body),
            () => failed.Author.ShouldBe(inProgress.Author),
            () => failed.Url.ShouldBe(inProgress.Url),
            () => failed.DetectedAt.ShouldBe(inProgress.DetectedAt));
    }

    [Fact]
    public void WhenCreatedFromInProgress_SetsWorkerRunId_BranchName_FailureReason_FailedAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();
        string branchName = "foundry/1/add-feature";
        string failureReason = "Container exited with code 1";
        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);

        // Act
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            branchName,
            failureReason,
            "generic_failure",
            failedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(inProgress.WorkerRunId),
            () => failed.BranchName.ShouldBe(branchName),
            () => failed.FailureReason.ShouldBe(failureReason),
            () => failed.FailedAt.ShouldBe(failedAt));
    }

    [Fact]
    public void WhenCreatedFromInProgress_SetsPullRequestUrlEmpty()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).InProgress();

        // Act
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            "foundry/1/add-feature",
            "Container exited with code 1",
            "generic_failure",
            DateTimeOffset.UtcNow);

        // Assert
        failed.PullRequestUrl.ShouldBe(string.Empty);
    }
}
