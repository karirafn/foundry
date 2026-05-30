using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.UnchangedIssueTests;

public sealed class Complete
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static UnchangedIssue CreateUnchangedIssue(MonitoredRepositoryId repositoryId)
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
        return inProgress.MarkUnchanged(Guid.NewGuid());
    }

    [Fact]
    public void WhenCompleted_ReturnsCompletedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = CreateUnchangedIssue(repositoryId);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        CompletedIssue completed = unchanged.Complete(completedAt);

        // Assert
        completed.Id.ShouldBe(unchanged.Id);
    }

    [Fact]
    public void WhenCompleted_RaisesIssueCompletedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = CreateUnchangedIssue(repositoryId);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        unchanged.Complete(completedAt);

        // Assert
        IssueCompleted domainEvent = unchanged.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueCompleted>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(unchanged.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenCompleted_CompletedIssueHasNullBranchAndPullRequest()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = CreateUnchangedIssue(repositoryId);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        CompletedIssue completed = unchanged.Complete(completedAt);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.BranchName.ShouldBeNull(),
            () => completed.PullRequestUrl.ShouldBeNull(),
            () => completed.CompletedAt.ShouldBe(completedAt),
            () => completed.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
