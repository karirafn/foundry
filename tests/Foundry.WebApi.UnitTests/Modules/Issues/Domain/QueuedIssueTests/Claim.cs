using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

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
        Guid workerRunId = Guid.NewGuid();

        // Act
        InProgressIssue inProgress = queued.Claim(workerRunId);

        // Assert
        inProgress.Id.ShouldBe(queued.Id);
    }

    [Fact]
    public void WhenClaimed_DoesNotRaiseDomainEvents()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);

        // Act — claim is acknowledged via integration event choreography, not domain events
        queued.Claim(Guid.NewGuid());

        // Assert
        queued.DomainEvents.ShouldBeEmpty();
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
