using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.PrincipleSectionSetTests;

public sealed class Of
{
    [Fact]
    public void WhenSectionsAreListedOutOfOrder_EnumeratesThemInTheFixedOrder()
    {
        // Act
        PrincipleSectionSet set = PrincipleSectionSet.Of(
            PrincipleSection.DomainModelling,
            PrincipleSection.Testing,
            PrincipleSection.Code);

        // Assert
        set.InOrder.ShouldBe([PrincipleSection.Testing, PrincipleSection.Code, PrincipleSection.DomainModelling]);
    }

    [Fact]
    public void WhenTwoSetsHoldTheSameSectionsInAnyOrder_TheyAreEqual()
    {
        // Act
        PrincipleSectionSet first = PrincipleSectionSet.Of(PrincipleSection.Code, PrincipleSection.Design);
        PrincipleSectionSet second = PrincipleSectionSet.Of(PrincipleSection.Design, PrincipleSection.Code);

        // Assert
        first.ShouldSatisfyAllConditions(
            () => first.ShouldBe(second),
            () => first.GetHashCode().ShouldBe(second.GetHashCode()));
    }

    [Fact]
    public void WhenASectionIsListedTwice_Throws()
    {
        // Act
        Action act = () => PrincipleSectionSet.Of(PrincipleSection.Design, PrincipleSection.Design);

        // Assert
        Should.Throw<ArgumentException>(act).Message.ShouldContain("only once");
    }

    [Fact]
    public void WhenASectionIsNotDefined_Throws()
    {
        // Act
        Action act = () => PrincipleSectionSet.Of((PrincipleSection)99);

        // Assert
        Should.Throw<ArgumentOutOfRangeException>(act);
    }

    [Fact]
    public void WhenNoSectionIsListed_IsEmpty()
    {
        // Act
        PrincipleSectionSet set = PrincipleSectionSet.Of();

        // Assert
        set.ShouldSatisfyAllConditions(
            () => set.IsEmpty.ShouldBeTrue(),
            () => set.ShouldBe(PrincipleSectionSet.None));
    }

    [Theory]
    [InlineData(PrincipleSection.Testing, "Testing")]
    [InlineData(PrincipleSection.Design, "Design")]
    [InlineData(PrincipleSection.Code, "Code")]
    [InlineData(PrincipleSection.DomainModelling, "Domain modelling")]
    public void EverySection_HasADisplayName(PrincipleSection section, string expected)
    {
        // Assert
        section.ToName().ShouldBe(expected);
    }
}
