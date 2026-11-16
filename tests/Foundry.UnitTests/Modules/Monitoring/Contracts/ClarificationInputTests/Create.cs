using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.ClarificationInputTests;

public sealed class Create
{
    [Fact]
    public void WhenGivenNoParts_Throws()
    {
        // Act + Assert
        Should.Throw<ArgumentException>(() => new ClarificationInput([]));
    }

    [Fact]
    public void WhenGivenAPart_KeepsIt()
    {
        // Act
        ClarificationInput input = new([new InputPart.Comments("Use Postgres.")]);

        // Assert
        input.Parts.ShouldHaveSingleItem().ShouldBe(new InputPart.Comments("Use Postgres."));
    }

    [Fact]
    public void FromComments_HoldsOneCommentsPart()
    {
        // Act
        ClarificationInput input = ClarificationInput.FromComments("Use Postgres.");

        // Assert
        input.Parts.ShouldHaveSingleItem().ShouldBe(new InputPart.Comments("Use Postgres."));
    }

    [Fact]
    public void TwoInputsWithEqualParts_AreEqual()
    {
        // Arrange
        ClarificationInput first = ClarificationInput.FromComments("Use Postgres.");
        ClarificationInput second = ClarificationInput.FromComments("Use Postgres.");

        // Act + Assert
        first.ShouldSatisfyAllConditions(
            () => first.ShouldBe(second),
            () => first.GetHashCode().ShouldBe(second.GetHashCode()));
    }

    [Fact]
    public void TwoInputsWithDifferentParts_AreNotEqual()
    {
        // Act + Assert
        ClarificationInput.FromComments("Use Postgres.").ShouldNotBe(ClarificationInput.FromComments("Use SQLite."));
    }
}
