using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
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
        issue.UpdateDetails(newTitle, newLabels, KindClassification.Unclassified);

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
        issue.UpdateDetails(newTitle, newLabels, KindClassification.Unclassified);

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
        issue.UpdateDetails(newTitle, newLabels, KindClassification.Unclassified);

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.Title.ShouldBe(newTitle),
            () => issue.Labels.ShouldBe(newLabels));
    }

    [Fact]
    public void WhenGivenLabelledClassification_ReplacesClassification()
    {
        // Arrange
        FreshQueuedIssue issue = new IssueBuilder()
            .WithKindClassification(KindClassification.Unclassified)
            .FreshQueued();
        KindClassification labelled = new KindClassification.Labelled(IssueKind.Refactor);

        // Act
        issue.UpdateDetails("Title", ["refactor"], labelled);

        // Assert
        issue.KindClassification.ShouldBe(labelled);
    }

    [Fact]
    public void WhenGivenUnclassified_ClearsLabelledClassification()
    {
        // Arrange
        FreshQueuedIssue issue = new IssueBuilder()
            .WithKindClassification(new KindClassification.Labelled(IssueKind.Bug))
            .FreshQueued();

        // Act
        issue.UpdateDetails("Title", ["foundry"], KindClassification.Unclassified);

        // Assert
        issue.KindClassification.ShouldBe(KindClassification.Unclassified);
    }
}
