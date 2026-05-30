using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.InProgressIssueTests;

public sealed class MarkFailed
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

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
        QueuedIssue queued = detected.Enqueue();
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
        FailedIssue failed = inProgress.MarkFailed(workerRunId, "Container exited with code 1", failedAt);

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
        inProgress.MarkFailed(workerRunId, "Container exited with code 1", failedAt);

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
        FailedIssue failed = inProgress.MarkFailed(workerRunId, failureReason, failedAt);

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
}
