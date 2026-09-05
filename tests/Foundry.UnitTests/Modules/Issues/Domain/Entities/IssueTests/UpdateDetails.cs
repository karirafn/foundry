using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

public sealed class UpdateDetails
{
    [Fact]
    public void WhenCalledOnDetectedIssue_UpdatesTitleAndLabels()
    {
        // Arrange
        DetectedIssue issue = new IssueBuilder().Detected();
        string newTitle = "Updated Title";
        IReadOnlyList<string> newLabels = ["foundry", "bug"];

        // Act
        issue.UpdateDetails(newTitle, newLabels);

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.Title.ShouldBe(newTitle),
            () => issue.Labels.ShouldBe(newLabels));
    }

    [Fact]
    public void WhenCalledOnQueuedIssue_UpdatesTitleAndLabels()
    {
        // Arrange
        FreshQueuedIssue issue = new IssueBuilder().FreshQueued();
        string newTitle = "Updated Title";
        IReadOnlyList<string> newLabels = ["foundry", "bug"];

        // Act
        issue.UpdateDetails(newTitle, newLabels);

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.Title.ShouldBe(newTitle),
            () => issue.Labels.ShouldBe(newLabels));
    }

    [Fact]
    public void WhenCalledOnBlockedIssue_UpdatesTitleAndLabels()
    {
        // Arrange
        BlockedIssue issue = new IssueBuilder().Detected().Block([42]);
        string newTitle = "Updated Title";
        IReadOnlyList<string> newLabels = ["foundry", "bug"];

        // Act
        issue.UpdateDetails(newTitle, newLabels);

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.Title.ShouldBe(newTitle),
            () => issue.Labels.ShouldBe(newLabels));
    }
}
