using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ReviewIssueTests;

public sealed class Fail
{
    [Fact]
    public void WhenFailed_ReturnsContinuableFailedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue failed = review.Fail("PR was closed without merge", FailureCategory.PrClosed, failedAt);

        // Assert
        failed.Id.ShouldBe(review.Id);
    }

    [Fact]
    public void WhenFailed_ContinuableFailedIssueHasWorkerRunIdFromReview()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue failed = review.Fail("PR was closed without merge", FailureCategory.PrClosed, failedAt);

        // Assert
        failed.WorkerRunId.ShouldBe(review.WorkerRunId);
    }

    [Fact]
    public void WhenFailed_ContinuableFailedIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();
        DateTimeOffset failedAt = new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero);
        string failureReason = "PR was closed without merge";

        // Act
        ContinuableFailedIssue failed = review.Fail(failureReason, FailureCategory.PrClosed, failedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.FailureReason.ShouldBe(failureReason),
            () => failed.FailedAt.ShouldBe(failedAt),
            () => failed.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => failed.BranchName.ShouldBe(review.BranchName),
            () => failed.PullRequestUrl.ShouldBe(review.PullRequestUrl));
    }

    [Fact]
    public void WhenFailed_RaisesIssueContinuableFailedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ReviewIssue review = new IssueBuilder().WithMonitoredRepositoryId(repositoryId).Review();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        review.Fail("PR was closed without merge", FailureCategory.PrClosed, failedAt);

        // Assert
        IssueContinuableFailed domainEvent = review.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuableFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(review.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
