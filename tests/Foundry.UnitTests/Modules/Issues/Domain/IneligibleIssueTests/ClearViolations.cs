using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.IneligibleIssueTests;

public sealed class ClearViolations
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static IneligibleIssue CreateIneligibleFromDetected(MonitoredRepositoryId repositoryId)
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
        return IneligibleIssue.FromDetected(detected, [EligibilityViolation.AllowDirectPushes()]);
    }

    private static IneligibleIssue CreateIneligibleWithBlockers(
        MonitoredRepositoryId repositoryId,
        IReadOnlyList<int> blockers)
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
        IneligibleIssue ineligible = IneligibleIssue.FromDetected(detected, [EligibilityViolation.AllowDirectPushes()]);
        ineligible.SetBlockedBy(blockers);
        return ineligible;
    }

    [Fact]
    public void WhenBlockedByIsEmpty_ReturnsQueuedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IneligibleIssue ineligible = CreateIneligibleFromDetected(repositoryId);

        // Act
        Issue result = ineligible.ClearViolations();

        // Assert
        QueuedIssue queued = result.ShouldBeOfType<QueuedIssue>();
        queued.Id.ShouldBe(ineligible.Id);
    }

    [Fact]
    public void WhenBlockedByIsEmpty_RaisesIssueQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IneligibleIssue ineligible = CreateIneligibleFromDetected(repositoryId);

        // Act
        ineligible.ClearViolations();

        // Assert
        IssueQueued domainEvent = ineligible.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(ineligible.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenBlockedByIsEmpty_QueuedIssueHasSameSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IneligibleIssue ineligible = CreateIneligibleFromDetected(repositoryId);

        // Act
        Issue result = ineligible.ClearViolations();

        // Assert
        QueuedIssue queued = result.ShouldBeOfType<QueuedIssue>();
        queued.ShouldSatisfyAllConditions(
            () => queued.MonitoredRepositoryId.ShouldBe(ineligible.MonitoredRepositoryId),
            () => queued.IssueNumber.ShouldBe(ineligible.IssueNumber),
            () => queued.Title.ShouldBe(ineligible.Title),
            () => queued.Body.ShouldBe(ineligible.Body),
            () => queued.Author.ShouldBe(ineligible.Author),
            () => queued.Url.ShouldBe(ineligible.Url),
            () => queued.Labels.ShouldBe(ineligible.Labels),
            () => queued.DetectedAt.ShouldBe(ineligible.DetectedAt));
    }

    [Fact]
    public void WhenBlockedByHasItems_ReturnsBlockedIssueWithSameId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IneligibleIssue ineligible = CreateIneligibleWithBlockers(repositoryId, [5, 10]);

        // Act
        Issue result = ineligible.ClearViolations();

        // Assert
        BlockedIssue blocked = result.ShouldBeOfType<BlockedIssue>();
        blocked.Id.ShouldBe(ineligible.Id);
    }

    [Fact]
    public void WhenBlockedByHasItems_RaisesIssueBlockedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IneligibleIssue ineligible = CreateIneligibleWithBlockers(repositoryId, [5]);

        // Act
        ineligible.ClearViolations();

        // Assert
        IssueBlocked domainEvent = ineligible.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueBlocked>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(ineligible.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenBlockedByHasItems_BlockedIssuePreservesBlockers()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IReadOnlyList<int> blockers = [5, 10];
        IneligibleIssue ineligible = CreateIneligibleWithBlockers(repositoryId, blockers);

        // Act
        Issue result = ineligible.ClearViolations();

        // Assert
        BlockedIssue blocked = result.ShouldBeOfType<BlockedIssue>();
        blocked.BlockedBy.ShouldBe(blockers);
    }
}
