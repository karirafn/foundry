using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.DetectedIssueTests;

public sealed class Detect
{
    [Fact]
    public void WhenAllParametersAreValid_ReturnsDetectedIssueWithCorrectProperties()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        int issueNumber = 42;
        string title = "Implement feature X";
        IssueAuthor author = IssueAuthor.Create("octocat").ValueOrThrow();
        ProviderUrl url = ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();
        IReadOnlyList<string> labels = ["foundry", "enhancement"];
        DateTimeOffset detectedAt = DateTimeOffset.UtcNow;

        // Act
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber,
            title,
            author,
            url,
            labels,
            detectedAt,
            KindClassification.Unclassified);

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => issue.IssueNumber.ShouldBe(issueNumber),
            () => issue.Title.ShouldBe(title),
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
        IssueAuthor author = IssueAuthor.Create("octocat").ValueOrThrow();
        ProviderUrl url = ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

        // Act
        DetectedIssue a = DetectedIssue.Detect(
            repositoryId, 1, "Title", author, url, [], DateTimeOffset.UtcNow, KindClassification.Unclassified);
        DetectedIssue b = DetectedIssue.Detect(
            repositoryId, 2, "Title", author, url, [], DateTimeOffset.UtcNow, KindClassification.Unclassified);

        // Assert
        a.Id.ShouldNotBe(b.Id);
    }

    [Fact]
    public void WhenCalled_BlockedByIsEmpty()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IssueAuthor author = IssueAuthor.Create("octocat").ValueOrThrow();
        ProviderUrl url = ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

        // Act
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId, 1, "Title", author, url, [], DateTimeOffset.UtcNow, KindClassification.Unclassified);

        // Assert
        issue.BlockedBy.ShouldBeEmpty();
    }

    [Fact]
    public void WhenLabelledClassificationProvided_SetsClassificationOnIssue()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IssueAuthor author = IssueAuthor.Create("octocat").ValueOrThrow();
        ProviderUrl url = ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

        // Act
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId,
            issueNumber: 1,
            title: "Title",
            author: author,
            url: url,
            labels: [],
            detectedAt: DateTimeOffset.UtcNow,
            kindClassification: new KindClassification.Labelled(IssueKind.Bug));

        // Assert
        issue.KindClassification.ShouldBe(new KindClassification.Labelled(IssueKind.Bug));
    }

    [Fact]
    public void WhenUnclassifiedProvided_SetsUnclassified()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        IssueAuthor author = IssueAuthor.Create("octocat").ValueOrThrow();
        ProviderUrl url = ProviderUrl.Create("https://github.com/owner/repo/issues/1").ValueOrThrow();

        // Act
        DetectedIssue issue = DetectedIssue.Detect(
            repositoryId, 1, "Title", author, url, [], DateTimeOffset.UtcNow, KindClassification.Unclassified);

        // Assert
        issue.KindClassification.ShouldBe(KindClassification.Unclassified);
    }
}
