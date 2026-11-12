using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Providers.TrackerReads;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.TrackerReads.ReferencedIssueSelectionTests;

public sealed class Select
{
    private const int IssueNumber = 7;

    [Fact]
    public void WhenTheIssueLinksAndMentionsOthers_ListsLinksByRelationThenMentionsInFirstMentionOrder()
    {
        // Arrange
        IssueContent content = new IssueContentBuilder()
            .WithBody("Builds on #40, see also #41.")
            .WithComment("Same as #42 and #40 again.")
            .WithComment("Also #43.")
            .Build();
        IReadOnlyList<IssueReference> links =
        [
            new(21, IssueRelation.RelatesTo),
            new(22, IssueRelation.Blocks),
            new(23, IssueRelation.BlockedBy),
            new(24, IssueRelation.RelatesTo),
            new(25, IssueRelation.BlockedBy),
        ];

        // Act
        IReadOnlyList<IssueReference> selected = ReferencedIssueSelection.Select(IssueNumber, content, links);

        // Assert
        selected.ShouldBe(
        [
            new IssueReference(23, IssueRelation.BlockedBy),
            new IssueReference(25, IssueRelation.BlockedBy),
            new IssueReference(22, IssueRelation.Blocks),
            new IssueReference(21, IssueRelation.RelatesTo),
            new IssueReference(24, IssueRelation.RelatesTo),
            new IssueReference(40, IssueRelation.Mention),
            new IssueReference(41, IssueRelation.Mention),
            new IssueReference(42, IssueRelation.Mention),
            new IssueReference(43, IssueRelation.Mention),
        ]);
    }

    [Fact]
    public void WhenAnIssueIsLinkedAndMentioned_ListsItOnceWithItsLinkRelation()
    {
        // Arrange
        IssueContent content = new IssueContentBuilder()
            .WithBody("Needs #22 first, then #40.")
            .Build();
        IReadOnlyList<IssueReference> links = [new(22, IssueRelation.RelatesTo)];

        // Act
        IReadOnlyList<IssueReference> selected = ReferencedIssueSelection.Select(IssueNumber, content, links);

        // Assert
        selected.ShouldBe(
        [
            new IssueReference(22, IssueRelation.RelatesTo),
            new IssueReference(40, IssueRelation.Mention),
        ]);
    }

    [Fact]
    public void WhenMoreThanTenIssuesAreReferenced_KeepsTheFirstTenInSelectionOrder()
    {
        // Arrange
        IssueContent content = new IssueContentBuilder()
            .WithBody($"#{IssueNumber} #50 #51 #52 #53 #54 #55 #56 #57 #58 #59")
            .Build();
        IReadOnlyList<IssueReference> links = [new(20, IssueRelation.RelatesTo), new(50, IssueRelation.Blocks)];

        // Act
        IReadOnlyList<IssueReference> selected = ReferencedIssueSelection.Select(IssueNumber, content, links);

        // Assert
        selected.Select(reference => reference.Number).ShouldBe([50, 20, 51, 52, 53, 54, 55, 56, 57, 58]);
    }

    [Theory]
    [InlineData("see group/project#12")]
    [InlineData("see owner/repo#12")]
    [InlineData("entity &#123; here")]
    [InlineData("see !12")]
    [InlineData("colour #12abc")]
    [InlineData("zero #0")]
    [InlineData("ten digits #1234567890")]
    [InlineData("heading ##12")]
    [InlineData("version 1.#12")]
    [InlineData("slug-#12")]
    public void WhenTheTextHoldsSomethingThatIsNotASameProjectIssueMention_SelectsNothing(string body)
    {
        // Arrange
        IssueContent content = new IssueContentBuilder().WithBody(body).Build();

        // Act
        IReadOnlyList<IssueReference> selected = ReferencedIssueSelection.Select(IssueNumber, content, []);

        // Assert
        selected.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("#12", 12)]
    [InlineData("see #12 now", 12)]
    [InlineData("(#12)", 12)]
    [InlineData("fixes #12.", 12)]
    [InlineData("first\n#12", 12)]
    [InlineData("hex colour #123456", 123456)]
    public void WhenTheTextMentionsASameProjectIssue_SelectsIt(string body, int expectedNumber)
    {
        // Arrange
        IssueContent content = new IssueContentBuilder().WithBody(body).Build();

        // Act
        IReadOnlyList<IssueReference> selected = ReferencedIssueSelection.Select(IssueNumber, content, []);

        // Assert
        selected.ShouldBe([new IssueReference(expectedNumber, IssueRelation.Mention)]);
    }

    [Fact]
    public void WhenTheIssueLinksAndMentionsItself_LeavesItOut()
    {
        // Arrange
        IssueContent content = new IssueContentBuilder()
            .WithBody($"Follow-up to #{IssueNumber} and #40.")
            .Build();
        IReadOnlyList<IssueReference> links = [new(IssueNumber, IssueRelation.RelatesTo), new(22, IssueRelation.Blocks)];

        // Act
        IReadOnlyList<IssueReference> selected = ReferencedIssueSelection.Select(IssueNumber, content, links);

        // Assert
        selected.ShouldBe(
        [
            new IssueReference(22, IssueRelation.Blocks),
            new IssueReference(40, IssueRelation.Mention),
        ]);
    }

    [Fact]
    public void WhenAnIssueIsLinkedWithSeveralRelations_ListsItOnceWithTheStrongestRelation()
    {
        // Arrange
        IssueContent content = new IssueContentBuilder().Build();
        IReadOnlyList<IssueReference> links =
        [
            new(30, IssueRelation.RelatesTo),
            new(31, IssueRelation.Blocks),
            new(30, IssueRelation.Blocks),
            new(31, IssueRelation.BlockedBy),
            new(30, IssueRelation.RelatesTo),
        ];

        // Act
        IReadOnlyList<IssueReference> selected = ReferencedIssueSelection.Select(IssueNumber, content, links);

        // Assert
        selected.ShouldBe(
        [
            new IssueReference(31, IssueRelation.BlockedBy),
            new IssueReference(30, IssueRelation.Blocks),
        ]);
    }
}
