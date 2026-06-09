using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ContinuationQueuedIssueTests;

public sealed class FromContinuableFailed
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

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
        return ContinuableFailedIssue.FromInProgress(
            inProgress,
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "Implemented the core feature",
            "Container exited with code 1",
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void WhenCreatedFromContinuableFailed_ReturnsContinuationQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue continuable = CreateContinuableFailedIssue(repositoryId);

        // Act
        ContinuationQueuedIssue continuationQueued = ContinuationQueuedIssue.FromContinuableFailed(continuable);

        // Assert
        continuationQueued.Id.ShouldBe(continuable.Id);
    }

    [Fact]
    public void WhenCreatedFromContinuableFailed_HasBranchNameAndLatestProgressFromSource()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue continuable = CreateContinuableFailedIssue(repositoryId);

        // Act
        ContinuationQueuedIssue continuationQueued = ContinuationQueuedIssue.FromContinuableFailed(continuable);

        // Assert
        continuationQueued.ShouldSatisfyAllConditions(
            () => continuationQueued.BranchName.ShouldBe(continuable.BranchName),
            () => continuationQueued.LatestProgress.ShouldBe(continuable.LatestProgress));
    }

    [Fact]
    public void WhenCreatedFromContinuableFailed_CopiesSharedPropertiesFromSource()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue continuable = CreateContinuableFailedIssue(repositoryId);

        // Act
        ContinuationQueuedIssue continuationQueued = ContinuationQueuedIssue.FromContinuableFailed(continuable);

        // Assert
        continuationQueued.ShouldSatisfyAllConditions(
            () => continuationQueued.MonitoredRepositoryId.ShouldBe(continuable.MonitoredRepositoryId),
            () => continuationQueued.IssueNumber.ShouldBe(continuable.IssueNumber),
            () => continuationQueued.Title.ShouldBe(continuable.Title),
            () => continuationQueued.Body.ShouldBe(continuable.Body),
            () => continuationQueued.DetectedAt.ShouldBe(continuable.DetectedAt));
    }
}
