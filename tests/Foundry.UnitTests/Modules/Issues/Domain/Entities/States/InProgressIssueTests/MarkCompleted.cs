using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.InProgressIssueTests;

public sealed class MarkCompleted
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
    public void WhenMarkCompleted_ReturnsCompletedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);

        // Act
        CompletedIssue completed = inProgress.MarkCompleted(
            "feat/1-add-feature",
            "https://github.com/owner/repo/pull/5",
            DateTimeOffset.UtcNow);

        // Assert
        completed.Id.ShouldBe(inProgress.Id);
    }

    [Fact]
    public void WhenMarkCompleted_CompletedIssueHasCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);
        string branchName = "feat/1-add-feature";
        string pullRequestUrl = "https://github.com/owner/repo/pull/5";
        DateTimeOffset completedAt = DateTimeOffset.UtcNow;

        // Act
        CompletedIssue completed = inProgress.MarkCompleted(branchName, pullRequestUrl, completedAt);

        // Assert
        completed.ShouldSatisfyAllConditions(
            () => completed.BranchName.ShouldBe(branchName),
            () => completed.PullRequestUrl.ShouldBe(pullRequestUrl),
            () => completed.CompletedAt.ShouldBe(completedAt),
            () => completed.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => completed.IssueNumber.ShouldBe(inProgress.IssueNumber),
            () => completed.Title.ShouldBe(inProgress.Title),
            () => completed.Body.ShouldBe(inProgress.Body),
            () => completed.DetectedAt.ShouldBe(inProgress.DetectedAt));
    }

    [Fact]
    public void WhenMarkCompleted_RaisesIssueCompletedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        InProgressIssue inProgress = CreateInProgressIssue(repositoryId);

        // Act
        inProgress.MarkCompleted(
            "feat/1-add-feature",
            "https://github.com/owner/repo/pull/5",
            DateTimeOffset.UtcNow);

        // Assert
        IssueCompleted domainEvent = inProgress.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueCompleted>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(inProgress.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
