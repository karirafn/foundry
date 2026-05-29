using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Issues.Domain.IssueTests;

public sealed class UpdateDetails
{
    private static IssueAuthor ValidAuthor =>
        ((Result<IssueAuthor>.Success)IssueAuthor.Create("octocat")).Value;

    private static ProviderUrl ValidUrl =>
        ((Result<ProviderUrl>.Success)ProviderUrl.Create("https://github.com/owner/repo/issues/1")).Value;

    private static DetectedIssue CreateDetectedIssue() =>
        DetectedIssue.Detect(
            MonitoredRepositoryId.New(),
            issueNumber: 1,
            title: "Original Title",
            body: "Original body",
            author: ValidAuthor,
            url: ValidUrl,
            labels: ["original-label"],
            detectedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void WhenCalledOnDetectedIssue_UpdatesTitleBodyAndLabels()
    {
        // Arrange
        DetectedIssue issue = CreateDetectedIssue();
        string newTitle = "Updated Title";
        string newBody = "Updated body";
        IReadOnlyList<string> newLabels = ["foundry", "bug"];

        // Act
        issue.UpdateDetails(newTitle, newBody, newLabels);

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.Title.ShouldBe(newTitle),
            () => issue.Body.ShouldBe(newBody),
            () => issue.Labels.ShouldBe(newLabels));
    }

    [Fact]
    public void WhenCalledOnQueuedIssue_UpdatesTitleBodyAndLabels()
    {
        // Arrange
        DetectedIssue detected = CreateDetectedIssue();
        QueuedIssue issue = detected.Enqueue();
        string newTitle = "Updated Title";
        string newBody = "Updated body";
        IReadOnlyList<string> newLabels = ["foundry", "bug"];

        // Act
        issue.UpdateDetails(newTitle, newBody, newLabels);

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.Title.ShouldBe(newTitle),
            () => issue.Body.ShouldBe(newBody),
            () => issue.Labels.ShouldBe(newLabels));
    }

    [Fact]
    public void WhenCalledOnBlockedIssue_UpdatesTitleBodyAndLabels()
    {
        // Arrange
        DetectedIssue detected = CreateDetectedIssue();
        BlockedIssue issue = detected.Block([42]);
        string newTitle = "Updated Title";
        string newBody = "Updated body";
        IReadOnlyList<string> newLabels = ["foundry", "bug"];

        // Act
        issue.UpdateDetails(newTitle, newBody, newLabels);

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.Title.ShouldBe(newTitle),
            () => issue.Body.ShouldBe(newBody),
            () => issue.Labels.ShouldBe(newLabels));
    }
}
