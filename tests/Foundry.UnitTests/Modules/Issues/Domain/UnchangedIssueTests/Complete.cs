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
    public void WhenCompleted_ReturnsDismissedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = CreateUnchangedIssue(repositoryId);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        DismissedIssue dismissed = unchanged.Complete(completedAt);

        // Assert
        dismissed.Id.ShouldBe(unchanged.Id);
    }

    [Fact]
    public void WhenCompleted_RaisesIssueDismissedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = CreateUnchangedIssue(repositoryId);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        unchanged.Complete(completedAt);

        // Assert
        IssueDismissed domainEvent = unchanged.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueDismissed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(unchanged.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenCompleted_DismissedIssueHasCompletedAt()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        UnchangedIssue unchanged = CreateUnchangedIssue(repositoryId);
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        DismissedIssue dismissed = unchanged.Complete(completedAt);

        // Assert
        dismissed.ShouldSatisfyAllConditions(
            () => dismissed.CompletedAt.ShouldBe(completedAt),
            () => dismissed.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
