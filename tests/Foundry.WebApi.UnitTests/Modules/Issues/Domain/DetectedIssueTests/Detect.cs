using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Shared.Abstractions;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Domain.DetectedIssueTests;

public sealed class Detect
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    [Fact]
    public void WhenAllParametersAreValid_ReturnsDetectedIssueWithCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        int issueNumber = 42;
        string title = "Implement feature X";
        string body = "Details about feature X.";
        IssueAuthor author = ValidAuthor;
        ProviderUrl url = ValidUrl;
        IReadOnlyList<string> labels = ["foundry", "enhancement"];
        DateTimeOffset detectedAt = DateTimeOffset.UtcNow;

        // Act
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber,
            title,
            body,
            author,
            url,
            labels,
            detectedAt);

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => issue.IssueNumber.ShouldBe(issueNumber),
            () => issue.Title.ShouldBe(title),
            () => issue.Body.ShouldBe(body),
            () => issue.Author.ShouldBe(author),
            () => issue.Url.ShouldBe(url),
            () => issue.Labels.ShouldBe(labels),
            () => issue.DetectedAt.ShouldBe(detectedAt));
    }

    [Fact]
    public void WhenCalled_AssignsNewId()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IssueAuthor author = ValidAuthor;
        ProviderUrl url = ValidUrl;

        // Act
        DetectedIssue a = DetectedIssue.Detect(
            repositoryId, 1, "Title", "Body", author, url, [], DateTimeOffset.UtcNow);
        DetectedIssue b = DetectedIssue.Detect(
            repositoryId, 2, "Title", "Body", author, url, [], DateTimeOffset.UtcNow);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }
}
