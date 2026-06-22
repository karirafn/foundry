using Foundry.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.IssueTests;

public sealed class SetBlockedBy
{
    private static IssueAuthor ValidAuthor =>
        IssueAuthor.Create("octocat").ValueOrThrow();

    private static ProviderUrl ValidUrl =>
        ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

    private static DetectedIssue CreateDetectedIssue() =>
        DetectedIssue.Detect(
            MonitoredRepositoryId.New(),
            issueNumber: 1,
            title: "Issue",
            body: "Body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void WhenBlockerListExceedsCap_TruncatesTo50()
    {
        // Arrange
        DetectedIssue issue = CreateDetectedIssue();
        IReadOnlyList<int> blockers = Enumerable.Range(1, 60).ToList();

        // Act
        issue.SetBlockedBy(blockers);

        // Assert
        issue.BlockedBy.Count.ShouldBe(50);
    }

    [Fact]
    public void WhenBlockerListExceedsCap_KeepsFirstFiftyItems()
    {
        // Arrange
        DetectedIssue issue = CreateDetectedIssue();
        IReadOnlyList<int> blockers = Enumerable.Range(1, 60).ToList();

        // Act
        issue.SetBlockedBy(blockers);

        // Assert
        issue.BlockedBy.ShouldBe(Enumerable.Range(1, 50).ToList());
    }
}
