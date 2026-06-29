using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ContinuableFailedIssueTests;

public sealed class FromInProgress
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
        QueuedIssue queued = detected.Enqueue();
        return queued.Claim(Guid.NewGuid());
    }

    [Fact]
    public void WhenCreatedFromInProgress_CopiesSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            workerRunId,
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
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();
        string branchName = "foundry/1/add-feature";
        string failureReason = "Container exited with code 1";
        DateTimeOffset failedAt = new DateTimeOffset(2026, 6, 9, 10, 0, 0, TimeSpan.Zero);

        // Act
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            workerRunId,
            branchName,
            failureReason,
            "generic_failure",
            failedAt);

        // Assert
        failed.ShouldSatisfyAllConditions(
            () => failed.WorkerRunId.ShouldBe(workerRunId),
            () => failed.BranchName.ShouldBe(branchName),
            () => failed.FailureReason.ShouldBe(failureReason),
            () => failed.FailedAt.ShouldBe(failedAt));
    }

    [Fact]
    public void WhenCreatedFromInProgress_SetsPullRequestUrlEmpty()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);

        // Act
        ContinuableFailedIssue failed = inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "Container exited with code 1",
            "generic_failure",
            DateTimeOffset.UtcNow);

        // Assert
        failed.PullRequestUrl.ShouldBe(string.Empty);
    }
}
