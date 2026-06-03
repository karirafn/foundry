using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.IneligibleIssueTests;

public sealed class UpdateViolations
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static IneligibleIssue CreateIneligibleIssue(MonitoredRepositoryId repositoryId)
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

    [Fact]
    public void WhenCalled_ReplacesViolations()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IneligibleIssue ineligible = CreateIneligibleIssue(repositoryId);
        IReadOnlyList<EligibilityViolation> newViolations =
        [
            EligibilityViolation.AllowForcePushes(),
            EligibilityViolation.AllowDeletion()
        ];

        // Act
        ineligible.UpdateViolations(newViolations);

        // Assert
        ineligible.Violations.ShouldBe(newViolations);
    }

    [Fact]
    public void WhenViolationsIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IneligibleIssue ineligible = CreateIneligibleIssue(repositoryId);
        IReadOnlyList<EligibilityViolation> newViolations = [];

        // Act & Assert
        Should.Throw<ArgumentException>(() => ineligible.UpdateViolations(newViolations));
    }
}
