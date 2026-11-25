using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.IssueBodyHashTests;

public sealed class OfSpec
{
    private const string Spec =
        "## Acceptance Criteria\n- [ ] The first criterion holds.\n- [ ] The second criterion holds.\n";

    [Fact]
    public void WhenABoxIsTicked_TheHashIsUnchanged()
    {
        // Act
        IssueBodyHash ticked = IssueBodyHash.OfSpec(
            "## Acceptance Criteria\n- [x] The first criterion holds.\n- [ ] The second criterion holds.\n");

        // Assert
        ticked.ShouldBe(IssueBodyHash.OfSpec(Spec));
    }

    [Fact]
    public void WhenABoxIsUntickedAndAnotherTicked_TheHashIsUnchanged()
    {
        // Arrange
        IssueBodyHash firstTicked = IssueBodyHash.OfSpec(
            "## Acceptance Criteria\n- [x] The first criterion holds.\n- [ ] The second criterion holds.\n");

        // Act
        IssueBodyHash secondTicked = IssueBodyHash.OfSpec(
            "## Acceptance Criteria\n- [ ] The first criterion holds.\n- [X] The second criterion holds.\n");

        // Assert
        secondTicked.ShouldBe(firstTicked);
    }

    [Theory]
    [InlineData("* [x] The criterion holds.", "* [ ] The criterion holds.")]
    [InlineData("+ [X] The criterion holds.", "+ [ ] The criterion holds.")]
    [InlineData("1. [x] The criterion holds.", "1. [ ] The criterion holds.")]
    [InlineData("  - [x] A nested criterion holds.", "  - [ ] A nested criterion holds.")]
    public void WhenAnyListMarkerIsTicked_TheHashIsUnchanged(string ticked, string unticked)
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.OfSpec(ticked);

        // Assert
        hash.ShouldBe(IssueBodyHash.OfSpec(unticked));
    }

    [Fact]
    public void WhenATickedMarkerIsInsideAFencedCodeBlockAtALineStart_ItIsNormalizedToo()
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.OfSpec("```markdown\n- [x] example\n```");

        // Assert
        hash.ShouldBe(IssueBodyHash.OfSpec("```markdown\n- [ ] example\n```"));
    }

    [Fact]
    public void WhenACriterionIsReworded_TheHashChanges()
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.OfSpec(
            "## Acceptance Criteria\n- [ ] The first criterion always holds.\n- [ ] The second criterion holds.\n");

        // Assert
        hash.ShouldNotBe(IssueBodyHash.OfSpec(Spec));
    }

    [Fact]
    public void WhenACriterionIsAdded_TheHashChanges()
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.OfSpec(Spec + "- [ ] A third criterion holds.\n");

        // Assert
        hash.ShouldNotBe(IssueBodyHash.OfSpec(Spec));
    }

    [Fact]
    public void WhenATypoIsFixed_TheHashChanges()
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.OfSpec(
            "## Acceptance Criteria\n- [ ] The frist criterion holds.\n- [ ] The second criterion holds.\n");

        // Assert
        hash.ShouldNotBe(IssueBodyHash.OfSpec(Spec));
    }

    [Fact]
    public void WhenABracketedXIsNotAListItemMarker_TheHashChanges()
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.OfSpec("Choose [x] or [ ].");

        // Assert
        hash.ShouldNotBe(IssueBodyHash.OfSpec("Choose [ ] or [ ]."));
    }

    [Fact]
    public void WhenBodiesDifferOnlyInLineEndings_TheyHashEqual()
    {
        // Act
        IssueBodyHash hash = IssueBodyHash.OfSpec("- [x] one\r\n- [ ] two\r\n");

        // Assert
        hash.ShouldBe(IssueBodyHash.OfSpec("- [ ] one\n- [ ] two"));
    }

    [Fact]
    public void WhenTheBodyIsNull_Throws()
    {
        // Act / Assert
        Should.Throw<ArgumentNullException>(() => IssueBodyHash.OfSpec(null!));
    }
}
