using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.IneligibleIssueTests;

public sealed class FromDetected
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static DetectedIssue CreateDetectedIssue(MonitoredRepositoryId repositoryId)
    {
        return DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Test Issue",
            body: "Test body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["foundry"],
            detectedAt: DateTimeOffset.UtcNow);
    }

    [Fact]
    public void WhenCalled_PreservesIssueId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        IReadOnlyList<EligibilityViolation> violations = [EligibilityViolation.AllowDirectPushes()];

        // Act
        IneligibleIssue ineligible = IneligibleIssue.FromDetected(detected, violations);

        // Assert
        ineligible.Id.ShouldBe(detected.Id);
    }

    [Fact]
    public void WhenCalled_PreservesSharedProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        IReadOnlyList<EligibilityViolation> violations = [EligibilityViolation.AllowDirectPushes()];

        // Act
        IneligibleIssue ineligible = IneligibleIssue.FromDetected(detected, violations);

        // Assert
        ineligible.ShouldSatisfyAllConditions(
            () => ineligible.MonitoredRepositoryId.ShouldBe(detected.MonitoredRepositoryId),
            () => ineligible.IssueNumber.ShouldBe(detected.IssueNumber),
            () => ineligible.Title.ShouldBe(detected.Title),
            () => ineligible.Body.ShouldBe(detected.Body),
            () => ineligible.Author.ShouldBe(detected.Author),
            () => ineligible.Url.ShouldBe(detected.Url),
            () => ineligible.Labels.ShouldBe(detected.Labels),
            () => ineligible.DetectedAt.ShouldBe(detected.DetectedAt));
    }

    [Fact]
    public void WhenCalled_StoresViolations()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DetectedIssue detected = CreateDetectedIssue(repositoryId);
        IReadOnlyList<EligibilityViolation> violations =
        [
            EligibilityViolation.AllowDirectPushes(),
            EligibilityViolation.AllowForcePushes()
        ];

        // Act
        IneligibleIssue ineligible = IneligibleIssue.FromDetected(detected, violations);

        // Assert
        ineligible.Violations.ShouldBe(violations);
    }
}
