using Foundry.Modules.Issues.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Contracts.EligibilityViolationDtoTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_HoldsProvidedValues()
    {
        // Arrange
        const string rule = "branch-protection:allow-direct-pushes";
        const string description = "The repository allows direct pushes.";

        // Act
        EligibilityViolationDto dto = new(rule, description);

        // Assert
        dto.ShouldSatisfyAllConditions(
            () => dto.Rule.ShouldBe(rule),
            () => dto.Description.ShouldBe(description));
    }
}
