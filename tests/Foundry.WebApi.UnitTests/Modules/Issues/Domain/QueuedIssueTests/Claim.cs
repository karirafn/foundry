using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Workers.Domain;
using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Domain.QueuedIssueTests;

public sealed class Claim
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static QueuedIssue CreateQueuedIssue(MonitoredRepositoryId repositoryId)
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
        return detected.Enqueue();
    }

    [Fact]
    public void WhenClaimed_ReturnsInProgressIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);

        // Act
        InProgressIssue inProgress = queued.Claim();

        // Assert
        inProgress.Id.ShouldBe(queued.Id);
    }

    [Fact]
    public void WhenClaimed_RaisesIssueClaimedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);

        // Act
        queued.Claim();

        // Assert
        IssueClaimed domainEvent = queued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueClaimed>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(queued.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenClaimed_SharedPropertiesAreCopied()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);

        // Act
        InProgressIssue inProgress = queued.Claim();

        // Assert
        inProgress.ShouldSatisfyAllConditions(
            () => inProgress.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => inProgress.IssueNumber.ShouldBe(1),
            () => inProgress.Title.ShouldBe("Test Issue"),
            () => inProgress.Body.ShouldBe("Test body"),
            () => inProgress.Labels.ShouldBe(["foundry"]));
    }

    [Fact]
    public void WhenClaimed_WorkerRunIdIsAssigned()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);

        // Act
        InProgressIssue inProgress = queued.Claim();

        // Assert
        inProgress.WorkerRunId.ShouldNotBe(default(WorkerRunId));
    }
}
