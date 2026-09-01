using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

public sealed class UpdateDetails
{
    [Fact]
    public void WhenCalledOnDetectedIssue_UpdatesTitleBodyAndLabels()
    {
        // Arrange
        DetectedIssue issue = new IssueBuilder().Detected();
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
        FreshQueuedIssue issue = new IssueBuilder().FreshQueued();
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
        BlockedIssue issue = new IssueBuilder().Detected().Block([42]);
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
