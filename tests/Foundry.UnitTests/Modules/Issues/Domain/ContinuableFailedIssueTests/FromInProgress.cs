using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ContinuableFailedIssueTests;

public sealed class FromInProgress
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
    public void WhenCreatedFromInProgress_ReturnsContinuableFailedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();
        string branchName = "foundry/1/add-feature";
        string latestProgress = "Implemented the core feature";
        string failureReason = "Container exited with code 1";
        DateTimeOffset failedAt = DateTimeOffset.UtcNow;

        // Act
        ContinuableFailedIssue continuable = ContinuableFailedIssue.FromInProgress(
            inProgress,
            workerRunId,
            branchName,
            latestProgress,
            failureReason,
            failedAt);

        // Assert
        continuable.Id.ShouldBe(inProgress.Id);
    }

    [Fact]
    public void WhenCreatedFromInProgress_HasCorrectSpecificProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();
        string branchName = "foundry/1/add-feature";
        string latestProgress = "Implemented the core feature";
        string failureReason = "Container exited with code 1";
        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);

        // Act
        ContinuableFailedIssue continuable = ContinuableFailedIssue.FromInProgress(
            inProgress,
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
            () => continuable.FailedAt.ShouldBe(failedAt));
    }

    [Fact]
    public void WhenCreatedFromInProgress_CopiesSharedPropertiesFromSource()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        ContinuableFailedIssue continuable = ContinuableFailedIssue.FromInProgress(
            inProgress,
            workerRunId,
            "foundry/1/add-feature",
            "some progress",
            "Container exited",
            DateTimeOffset.UtcNow);

        // Assert
        continuable.ShouldSatisfyAllConditions(
            () => continuable.MonitoredRepositoryId.ShouldBe(inProgress.MonitoredRepositoryId),
            () => continuable.IssueNumber.ShouldBe(inProgress.IssueNumber),
            () => continuable.Title.ShouldBe(inProgress.Title),
            () => continuable.Body.ShouldBe(inProgress.Body),
            () => continuable.DetectedAt.ShouldBe(inProgress.DetectedAt));
    }
}
