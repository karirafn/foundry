using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.InProgressIssueTests;

public sealed class MarkFailed
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private static InProgressIssue CreateInProgressIssue(MonitoredRepositoryId repositoryId)
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
        FreshQueuedIssue queued = detected.Enqueue();
        return queued.Claim(Guid.NewGuid());
    }

    [Fact]
    public void WhenMarkedFailed_ReturnsFailedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        FailedIssue failed = inProgress.MarkFailed(workerRunId, "Container exited with code 1", failedAt, "generic_failure");

        // Assert
        failed.Id.ShouldBe(inProgress.Id);
    }

    [Fact]
    public void WhenMarkedFailed_RaisesIssueFailedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        inProgress.MarkFailed(workerRunId, "Container exited with code 1", failedAt, "generic_failure");

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
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();
        string failureReason = "Container exited with code 1";
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        FailedIssue failed = inProgress.MarkFailed(workerRunId, failureReason, failedAt, "generic_failure");

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(workerRunId),
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
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        FailedIssue failed = inProgress.MarkFailed(workerRunId, "Usage limit reached", failedAt, "usage_limited");

        // Assert
        failed.FailureCategory.ShouldBe("usage_limited");
    }
}
