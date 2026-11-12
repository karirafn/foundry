using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Roles;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Roles.WorkerPrinciplesTests;

public sealed class AssembleFor
{
    private static WorkerPrinciples Principles() =>
        new(
            "FRAMING",
            new Dictionary<PrincipleSection, string>
            {
                [PrincipleSection.DomainModelling] = "DOMAIN",
                [PrincipleSection.Code] = "CODE",
                [PrincipleSection.Design] = "DESIGN",
                [PrincipleSection.Testing] = "TESTING",
            });

    [Fact]
    public void WhenEverySectionIsSelected_PlacesTheFramingThenEachSectionInTheFixedOrder()
    {
        // Act
        string text = Principles().AssembleFor(WorkerRole.Implement.PrincipleSections);

        // Assert
        text.ShouldBe("FRAMING\n\nTESTING\n\nDESIGN\n\nCODE\n\nDOMAIN");
    }

    [Fact]
    public void WhenSomeSectionsAreSelected_LeavesOutTheOthers()
    {
        // Act
        string text = Principles().AssembleFor(WorkerRole.Triage.PrincipleSections);

        // Assert
        text.ShouldBe("FRAMING\n\nDESIGN\n\nDOMAIN");
    }

    [Fact]
    public void WhenTheSelectionListsSectionsOutOfOrder_StillUsesTheFixedOrder()
    {
        // Arrange
        WorkerPrinciples principles = Principles();

        // Act
        string reversed = principles.AssembleFor(PrincipleSectionSet.Of(PrincipleSection.Code, PrincipleSection.Testing));
        string ordered = principles.AssembleFor(PrincipleSectionSet.Of(PrincipleSection.Testing, PrincipleSection.Code));

        // Assert
        reversed.ShouldSatisfyAllConditions(
            () => reversed.ShouldBe("FRAMING\n\nTESTING\n\nCODE"),
            () => reversed.ShouldBe(ordered));
    }

    [Fact]
    public void WhenNothingIsSelected_ReturnsNeitherTheFramingNorAnySection()
    {
        // Act
        string text = Principles().AssembleFor(PrincipleSectionSet.None);

        // Assert
        text.ShouldBeEmpty();
    }

    [Fact]
    public void WhenASectionHasNoText_RejectsThePrinciples()
    {
        // Act
        Action act = () => _ = new WorkerPrinciples(
            "FRAMING",
            new Dictionary<PrincipleSection, string> { [PrincipleSection.Design] = "DESIGN" });

        // Assert
        Should.Throw<ArgumentException>(act).Message.ShouldContain("Testing");
    }
}
