using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.InProgressIssueTests;

public sealed class MarkContinuableFailed
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
    public void WhenMarkedContinuableFailed_ReturnsContinuableFailedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue continuable = inProgress.MarkContinuableFailed(
            workerRunId,
            "foundry/1/add-feature",
            "Implemented the core feature",
            "Container exited with code 1",
            failedAt);

        // Assert
        continuable.Id.ShouldBe(inProgress.Id);
    }

    [Fact]
    public void WhenMarkedContinuableFailed_RaisesIssueContinuableFailedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        inProgress.MarkContinuableFailed(
            workerRunId,
            "foundry/1/add-feature",
            "Implemented the core feature",
            "Container exited with code 1",
            failedAt);

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
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();
        string branchName = "foundry/1/add-feature";
        string latestProgress = "Implemented the core feature";
        string failureReason = "Container exited with code 1";
        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

        // Act
        ContinuableFailedIssue continuable = inProgress.MarkContinuableFailed(
            workerRunId,
            branchName,
            latestProgress,
            failureReason,
            failedAt);

        // Assert
        continuable.ShouldSatisfyAllConditions(
            () => continuable.WorkerRunId.ShouldBe(workerRunId),
            () => continuable.BranchName.ShouldBe(branchName),
            () => continuable.LatestProgress.ShouldBe(latestProgress),
            () => continuable.FailureReason.ShouldBe(failureReason),
            () => continuable.FailedAt.ShouldBe(failedAt),
            () => continuable.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => continuable.IssueNumber.ShouldBe(inProgress.IssueNumber),
            () => continuable.Title.ShouldBe(inProgress.Title),
            () => continuable.DetectedAt.ShouldBe(inProgress.DetectedAt));
    }
}
