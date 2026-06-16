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
        return inProgress.MarkContinuableFailed(
            Guid.NewGuid(),
            "foundry/1/add-feature",
            "Container exited with code 1",
            DateTimeOffset.UtcNow);
    }

    [Fact]
    public void WhenCreatedFromContinuableFailed_CopiesSharedPropertiesAndBranchName()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ContinuableFailedIssue failed = CreateContinuableFailedIssue(repositoryId);

        // Act
        ContinuationQueuedIssue queued = ContinuationQueuedIssue.FromContinuableFailed(failed);

        // Assert
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(failed.Id),
            () => queued.MonitoredRepositoryId.ShouldBe(failed.MonitoredRepositoryId),
            () => queued.IssueNumber.ShouldBe(failed.IssueNumber),
            () => queued.Title.ShouldBe(failed.Title),
            () => queued.Body.ShouldBe(failed.Body),
            () => queued.Author.ShouldBe(failed.Author),
            () => queued.DetectedAt.ShouldBe(failed.DetectedAt),
            () => queued.BranchName.ShouldBe(failed.BranchName));
    }
}
