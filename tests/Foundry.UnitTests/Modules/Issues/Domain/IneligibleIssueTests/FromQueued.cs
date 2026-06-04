using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.IneligibleIssueTests;

public sealed class FromQueued
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
    public void WhenCalled_PreservesIssueId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        IReadOnlyList<EligibilityViolation> violations = [EligibilityViolation.AllowDirectPushes()];

        // Act
        IneligibleIssue ineligible = IneligibleIssue.FromQueued(queued, violations);

        // Assert
        ineligible.Id.ShouldBe(queued.Id);
    }

    [Fact]
    public void WhenCalled_PreservesSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        IReadOnlyList<EligibilityViolation> violations = [EligibilityViolation.AllowDirectPushes()];

        // Act
        IneligibleIssue ineligible = IneligibleIssue.FromQueued(queued, violations);

        // Assert
        ineligible.ShouldSatisfyAllConditions(
            () => ineligible.MonitoredRepositoryId.ShouldBe(queued.MonitoredRepositoryId),
            () => ineligible.IssueNumber.ShouldBe(queued.IssueNumber),
            () => ineligible.Title.ShouldBe(queued.Title),
            () => ineligible.Body.ShouldBe(queued.Body),
            () => ineligible.Author.ShouldBe(queued.Author),
            () => ineligible.Url.ShouldBe(queued.Url),
            () => ineligible.Labels.ShouldBe(queued.Labels),
            () => ineligible.DetectedAt.ShouldBe(queued.DetectedAt));
    }

    [Fact]
    public void WhenCalled_StoresViolations()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        IReadOnlyList<EligibilityViolation> violations =
        [
            EligibilityViolation.AllowDirectPushes(),
            EligibilityViolation.AllowForcePushes()
        ];

        // Act
        IneligibleIssue ineligible = IneligibleIssue.FromQueued(queued, violations);

        // Assert
        ineligible.Violations.ShouldBe(violations);
    }

    [Fact]
    public void WhenViolationsIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        QueuedIssue queued = CreateQueuedIssue(repositoryId);
        IReadOnlyList<EligibilityViolation> violations = [];

        // Act & Assert
        Should.Throw<ArgumentException>(() => IneligibleIssue.FromQueued(queued, violations));
    }
}
