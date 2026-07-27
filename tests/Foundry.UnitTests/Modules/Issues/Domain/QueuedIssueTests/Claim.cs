using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.QueuedIssueTests;

public sealed class Claim
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

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
        Guid workerRunId = Guid.NewGuid();

        // Act
        InProgressIssue inProgress = queued.Claim(workerRunId);

        // Assert
        inProgress.Id.ShouldBe(queued.Id);
    }

    [Fact]
    public void WhenClaimed_RaisesIssueInProgressDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);

        // Act
        queued.Claim(Guid.NewGuid());

        // Assert
        IssueInProgress domainEvent = queued.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueInProgress>();
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
        InProgressIssue inProgress = queued.Claim(Guid.NewGuid());

        // Assert
        inProgress.ShouldSatisfyAllConditions(
            () => inProgress.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => inProgress.IssueNumber.ShouldBe(1),
            () => inProgress.Title.ShouldBe("Test Issue"),
            () => inProgress.Body.ShouldBe("Test body"),
            () => inProgress.Labels.ShouldBe(["foundry"]));
    }

    [Fact]
    public void WhenClaimed_WorkerRunIdMatchesProvidedGuid()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        InProgressIssue inProgress = queued.Claim(workerRunId);

        // Assert
        inProgress.WorkerRunId.ShouldBe(workerRunId);
    }
}
