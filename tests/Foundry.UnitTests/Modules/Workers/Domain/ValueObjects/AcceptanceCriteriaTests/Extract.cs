using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.AcceptanceCriteriaTests;

public sealed class Extract
{
    [Fact]
    public void WhenTheBodyHasACriteriaHeading_NumbersItsListItems()
    {
        // Arrange
        const string body = """
            ## Summary

            - not a criterion

            ## Acceptance Criteria

            - [ ] first
            - [x] second
            * third
            1. fourth

            ## Scope

            - not a criterion either
            """;

        // Act
        AcceptanceCriteria criteria = AcceptanceCriteria.Extract(body);

        // Assert
        criteria.Items.Select(item => (item.Id, item.Text)).ShouldBe(
            [("AC1", "first"), ("AC2", "second"), ("AC3", "third"), ("AC4", "fourth")]);
    }

    [Theory]
    [InlineData("# acceptance criteria")]
    [InlineData("### ACCEPTANCE CRITERIA")]
    [InlineData("## Acceptance criteria:")]
    public void MatchesTheHeadingAtAnyLevelCaseInsensitively(string heading)
    {
        // Act
        AcceptanceCriteria criteria = AcceptanceCriteria.Extract($"{heading}\n\n- only\n");

        // Assert
        criteria.Items.Select(item => item.Text).ShouldBe(["only"]);
    }

    [Fact]
    public void ADeeperHeadingDoesNotEndTheSection()
    {
        // Arrange
        const string body = "## Acceptance Criteria\n\n- one\n\n### Errors\n\n- two\n\n## Notes\n\n- three\n";

        // Act
        AcceptanceCriteria criteria = AcceptanceCriteria.Extract(body);

        // Assert
        criteria.Items.Select(item => item.Text).ShouldBe(["one", "two"]);
    }

    [Fact]
    public void AnItemSpanningLinesJoinsItsContinuationLines()
    {
        // Arrange
        const string body = "## Acceptance Criteria\n\n- [ ] Given a thing,\n  when it runs, then it returns:\n  - `A` when x;\n  - `B` otherwise.\n- second\n";

        // Act
        AcceptanceCriteria criteria = AcceptanceCriteria.Extract(body);

        // Assert
        criteria.Items.Select(item => item.Text).ShouldBe(
            ["Given a thing, when it runs, then it returns: - `A` when x; - `B` otherwise.", "second"]);
    }

    [Fact]
    public void WhenTwoCriteriaHeadingsExist_TheFirstWins()
    {
        // Arrange
        const string body = "## Acceptance Criteria\n\n- first\n\n## Acceptance Criteria\n\n- second\n";

        // Act
        AcceptanceCriteria criteria = AcceptanceCriteria.Extract(body);

        // Assert
        criteria.Items.Select(item => item.Text).ShouldBe(["first"]);
    }

    [Fact]
    public void AHeadingInsideACodeFenceIsNotAHeading()
    {
        // Arrange
        const string body = "```\n## Acceptance Criteria\n- fenced\n```\n\n## Acceptance Criteria\n\n- real\n";

        // Act
        AcceptanceCriteria criteria = AcceptanceCriteria.Extract(body);

        // Assert
        criteria.Items.Select(item => item.Text).ShouldBe(["real"]);
    }

    [Fact]
    public void AParagraphAfterTheListIsNotPartOfTheLastItem()
    {
        // Arrange
        const string body = "## Acceptance Criteria\r\n\r\n- one\r\n\r\nSome closing remark.\r\n";

        // Act
        AcceptanceCriteria criteria = AcceptanceCriteria.Extract(body);

        // Assert
        criteria.Items.Select(item => item.Text).ShouldBe(["one"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("## Summary\n\n- a list item\n")]
    [InlineData("Acceptance Criteria\n\n- not under a heading\n")]
    public void WhenTheBodyHasNoCriteriaHeading_YieldsAnEmptyList(string body)
    {
        // Act
        AcceptanceCriteria criteria = AcceptanceCriteria.Extract(body);

        // Assert
        criteria.Items.ShouldBeEmpty();
    }
}
