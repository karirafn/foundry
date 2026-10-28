using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

public sealed class Fail
{
    [Fact]
    public void WhenFailed_ReturnsContinuableFailedIssueWithSameId()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();

        // Act
        ContinuableFailedIssue failed = checksPending.Fail("PR closed without merge", FailureCategory.PrClosed, DateTimeOffset.UtcNow);

        // Assert
        failed.Id.ShouldBe(checksPending.Id);
    }

    [Fact]
    public void WhenFailed_CarriesWorkerRunIdAndBranchNameAndPullRequestUrl()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithBranchName("feat/42-add-feature")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/42")
            .ChecksPending();

        // Act
        ContinuableFailedIssue failed = checksPending.Fail("PR closed without merge", FailureCategory.PrClosed, DateTimeOffset.UtcNow);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(checksPending.WorkerRunId),
            () => failed.BranchName.ShouldBe(checksPending.BranchName),
            () => failed.PullRequestUrl.ShouldBe(checksPending.PullRequestUrl));
    }

    [Fact]
    public void WhenFailed_SetsFailureReasonAndCategoryAndFailedAt()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder().ChecksPending();
        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        // Act
        ContinuableFailedIssue failed = checksPending.Fail("PR closed without merge", FailureCategory.PrClosed, failedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.FailureReason.ShouldBe("PR closed without merge"),
            () => failed.FailureCategory.ShouldBe(FailureCategory.PrClosed),
            () => failed.FailedAt.ShouldBe(failedAt));
    }

    [Fact]
    public void WhenFailed_RaisesIssueContinuableFailedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .ChecksPending();

        // Act
        checksPending.Fail("PR closed without merge", FailureCategory.PrClosed, DateTimeOffset.UtcNow);

        // Assert
        IssueContinuableFailed domainEvent = checksPending.DomainEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueContinuableFailed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(checksPending.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
