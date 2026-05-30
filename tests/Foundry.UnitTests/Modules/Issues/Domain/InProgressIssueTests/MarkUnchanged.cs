using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.InProgressIssueTests;

public sealed class MarkUnchanged
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
    public void WhenMarkedUnchanged_ReturnsUnchangedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        UnchangedIssue unchanged = inProgress.MarkUnchanged(workerRunId);

        // Assert
        unchanged.Id.ShouldBe(inProgress.Id);
    }

    [Fact]
    public void WhenMarkedUnchanged_RaisesIssueUnchangedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        inProgress.MarkUnchanged(workerRunId);

        // Assert
        IssueUnchanged domainEvent = inProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueUnchanged>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(inProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenMarkedUnchanged_UnchangedIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        Guid workerRunId = Guid.NewGuid();

        // Act
        UnchangedIssue unchanged = inProgress.MarkUnchanged(workerRunId);

        // Assert
        unchanged.ShouldSatisfyAllConditions(
            () => unchanged.WorkerRunId.ShouldBe(workerRunId),
            () => unchanged.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => unchanged.IssueNumber.ShouldBe(inProgress.IssueNumber),
            () => unchanged.Title.ShouldBe(inProgress.Title),
            () => unchanged.DetectedAt.ShouldBe(inProgress.DetectedAt));
    }
}
