using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ContinuableFailedIssueTests;

public sealed class Retry
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private static ContinuableFailedIssue CreateContinuableFailedIssue(MonitoredRepositoryId repositoryId)
    {
        DetectedIssue detected = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Test Issue",
            body: "Test body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: DateTimeOffset.UtcNow);
        QueuedIssue queued = detected.Enqueue();
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());
        return inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "Container exited with code 1",
            "generic_failure",
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void WhenRetried_ReturnsContinuationQueuedIssueWithBranchName()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue failed = CreateContinuableFailedIssue(repositoryId);

        // Act
        ContinuationQueuedIssue queued = failed.Retry();

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(failed.Id),
            () => queued.BranchName.ShouldBe(failed.BranchName));
    }

    [Fact]
    public void WhenRetried_RaisesIssueContinuationQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue failed = CreateContinuableFailedIssue(repositoryId);

        // Act
        failed.Retry();

        // Assert
        IssueContinuationQueued domainEvent = failed.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueContinuationQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(failed.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
