using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

public sealed class KindPrecedence
{
    [Fact]
    public void WhenAKindLabelIsAddedToATriagedIssue_TheLabelDecides()
    {
        // Arrange
        FreshQueuedIssue issue = new IssueBuilder()
            .WithKindClassification(KindClassification.Unclassified)
            .FreshQueuedFromTriage(IssueKind.Feature);

        // Act
        issue.UpdateDetails(issue.Title, ["bug"], new KindClassification.Labelled(IssueKind.Bug));

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.KindClassification.ShouldBe(new KindClassification.Labelled(IssueKind.Bug)),
            () => issue.ConflictingTriageKind.ShouldBe(IssueKind.Feature));
    }

    [Fact]
    public void WhenTheLastKindLabelIsRemovedFromATriagedIssue_ItStaysTriaged()
    {
        // Arrange
        FreshQueuedIssue issue = new IssueBuilder()
            .WithKindClassification(KindClassification.Unclassified)
            .FreshQueuedFromTriage(IssueKind.Refactor);
        issue.UpdateDetails(issue.Title, ["bug"], new KindClassification.Labelled(IssueKind.Bug));

        // Act
        issue.UpdateDetails(issue.Title, [], KindClassification.Unclassified);

        // Assert
        issue.ShouldSatisfyAllConditions(
            () => issue.KindClassification.ShouldBe(new KindClassification.Triaged(IssueKind.Refactor)),
            () => issue.ConflictingTriageKind.ShouldBeNull());
    }

    [Fact]
    public void WhenTheLabelAndTriageAgree_NoConflictIsRecorded()
    {
        // Arrange
        Issue issue = new IssueBuilder()
            .WithKindClassification(new KindClassification.Labelled(IssueKind.Bug))
            .FreshQueuedFromTriage(IssueKind.Bug);

        // Act
        IssueKind? conflict = issue.ConflictingTriageKind;

        // Assert
        conflict.ShouldBeNull();
    }

    [Fact]
    public void WhenTriaged_TheBranchPrefixFollowsTheTriagedKind()
    {
        // Arrange
        FreshQueuedIssue issue = new IssueBuilder()
            .WithKindClassification(KindClassification.Unclassified)
            .WithIssueNumber(12)
            .WithTitle("Tidy up")
            .FreshQueuedFromTriage(IssueKind.Refactor);

        // Act
        string? branchName = issue.DispatchBranchName?.Value;

        // Assert
        branchName.ShouldNotBeNull().ShouldStartWith("refactor/12-");
    }
}
