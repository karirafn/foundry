using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.AcceptanceCriteriaTests;

public sealed class Extract
{
    [Fact]
    public void WhenTheBodyHasAnAcceptanceCriteriaHeading_NumbersItsListItemsAndStripsTheirCheckboxes()
    {
        // Arrange
        const string body = """
            ## Summary

            - not a criterion

            ## Acceptance Criteria

            - [ ] Given a run, then it stops.
            - [x] Given a verdict, then it is stored.
            * Plain item.
            1. Numbered item.

            ## Type

            - feature
            """;

        // Act
        IReadOnlyList<AcceptanceCriterion> criteria = AcceptanceCriteria.Extract(body);

        // Assert
        criteria.ShouldBe([
            new AcceptanceCriterion(1, "Given a run, then it stops."),
            new AcceptanceCriterion(2, "Given a verdict, then it is stored."),
            new AcceptanceCriterion(3, "Plain item."),
            new AcceptanceCriterion(4, "Numbered item."),
        ]);
        criteria.Select(criterion => criterion.Id).ShouldBe(["AC1", "AC2", "AC3", "AC4"]);
    }

    [Theory]
    [InlineData("# ACCEPTANCE CRITERIA")]
    [InlineData("### acceptance criteria:")]
    [InlineData("#### Acceptance Criteria ##")]
    public void WhenTheHeadingIsAnyLevelOrCase_FindsIt(string heading)
    {
        // Arrange
        string body = heading + "\n\n- [ ] One.\n";

        // Act / Assert
        AcceptanceCriteria.Extract(body).ShouldBe([new AcceptanceCriterion(1, "One.")]);
    }

    [Fact]
    public void WhenASubheadingFollows_KeepsItsItemsUntilAHeadingOfTheSameOrHigherLevel()
    {
        // Arrange
        const string body = """
            ## Acceptance Criteria

            - One.

            ### Errors

            - Two.

            # Notes

            - Three.
            """;

        // Act / Assert
        AcceptanceCriteria.Extract(body).Select(criterion => criterion.Text).ShouldBe(["One.", "Two."]);
    }

    [Fact]
    public void WhenAnItemSpansLinesOrHasNestedItems_JoinsThemIntoItsText()
    {
        // Arrange
        const string body = """
            ## Acceptance Criteria

            - [ ] Given a dispatch, when the run starts,
              then it records its facts:
              - the pull request URL;
              - the patch-id.

              They are persisted.
            - [ ] Second.
            """;

        // Act / Assert
        AcceptanceCriteria.Extract(body).ShouldBe([
            new AcceptanceCriterion(
                1,
                "Given a dispatch, when the run starts, then it records its facts: the pull request URL; the patch-id. They are persisted."),
            new AcceptanceCriterion(2, "Second."),
        ]);
    }

    [Fact]
    public void WhenTwoHeadingsMatch_TheFirstWins()
    {
        // Arrange
        const string body = "## Acceptance Criteria\n\n- First.\n\n## Acceptance Criteria\n\n- Second.\n";

        // Act / Assert
        AcceptanceCriteria.Extract(body).ShouldBe([new AcceptanceCriterion(1, "First.")]);
    }

    [Fact]
    public void WhenTheHeadingIsInsideACodeFence_IgnoresIt()
    {
        // Arrange
        const string body = "```\n## Acceptance Criteria\n- Fenced.\n```\n";

        // Act / Assert
        AcceptanceCriteria.Extract(body).ShouldBeEmpty();
    }

    [Fact]
    public void WhenWindowsLineEndingsAreUsed_ExtractsTheItems()
    {
        // Act / Assert
        AcceptanceCriteria.Extract("## Acceptance Criteria\r\n\r\n- One.\r\n- Two.\r\n")
            .Select(criterion => criterion.Text)
            .ShouldBe(["One.", "Two."]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("## Summary\n\n- Something.\n")]
    [InlineData("Acceptance criteria\n\n- Not under a heading.\n")]
    public void WhenTheBodyHasNoAcceptanceCriteriaHeading_YieldsNoCriteria(string? body)
    {
        // Act / Assert
        AcceptanceCriteria.Extract(body).ShouldBeEmpty();
    }
}
