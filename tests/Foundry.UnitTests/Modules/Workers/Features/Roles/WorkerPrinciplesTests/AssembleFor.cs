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
    public void WhenASectionHasBullets_PrefixesEachBulletWithItsSectionIdAndPosition()
    {
        // Arrange
        WorkerPrinciples principles = new(
            "FRAMING",
            new Dictionary<PrincipleSection, string>
            {
                [PrincipleSection.Testing] = "Testing:\n- Tests first.\n- One behaviour each.",
                [PrincipleSection.Design] = "Design:\n- Safe defaults.",
                [PrincipleSection.Code] = "Code:\n- Comments explain why.",
                [PrincipleSection.DomainModelling] = "Domain modelling:\n- Domain terms.\n- One type per state.",
            });

        // Act
        string text = principles.AssembleFor(WorkerRole.Implement.PrincipleSections);

        // Assert
        text.ShouldBe(
            "FRAMING\n\n"
            + "Testing:\n- [testing.1] Tests first.\n- [testing.2] One behaviour each.\n\n"
            + "Design:\n- [design.1] Safe defaults.\n\n"
            + "Code:\n- [code.1] Comments explain why.\n\n"
            + "Domain modelling:\n- [domain.1] Domain terms.\n- [domain.2] One type per state.");
    }

    [Fact]
    public void WhenASectionLineIsNotABullet_LeavesItUnnumberedAndDoesNotCountIt()
    {
        // Arrange
        WorkerPrinciples principles = new(
            "FRAMING",
            new Dictionary<PrincipleSection, string>
            {
                [PrincipleSection.Testing] = "TESTING",
                [PrincipleSection.Design] = "Design:\n- First.\n  continued\n- Second.",
                [PrincipleSection.Code] = "CODE",
                [PrincipleSection.DomainModelling] = "DOMAIN",
            });

        // Act
        string text = principles.AssembleFor(PrincipleSectionSet.Of(PrincipleSection.Design));

        // Assert
        text.ShouldBe("FRAMING\n\nDesign:\n- [design.1] First.\n  continued\n- [design.2] Second.");
    }

    [Fact]
    public void IdsFor_ListsEveryBulletIdOfTheSelectedSectionsInOrder()
    {
        // Arrange
        WorkerPrinciples principles = new(
            "FRAMING",
            new Dictionary<PrincipleSection, string>
            {
                [PrincipleSection.Testing] = "Testing:\n- A.\n- B.",
                [PrincipleSection.Design] = "Design:\n- C.\n- D.",
                [PrincipleSection.Code] = "Code:\n- E.",
                [PrincipleSection.DomainModelling] = "Domain modelling:\n- F.",
            });

        // Act
        IReadOnlyList<string> ids = principles.IdsFor(WorkerRole.Triage.PrincipleSections);

        // Assert
        ids.ShouldBe(["design.1", "design.2", "domain.1"]);
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
