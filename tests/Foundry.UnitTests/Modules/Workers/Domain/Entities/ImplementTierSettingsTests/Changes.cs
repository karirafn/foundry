using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.ImplementTierSettingsTests;

public sealed class Changes
{
    [Fact]
    public void Create_StartsWithAnEmptyModelAndEffort()
    {
        // Act
        ImplementTierSettings settings = ImplementTierSettings.Create(ImplementDemand.Routine);

        // Assert
        settings.ShouldSatisfyAllConditions(
            () => settings.Demand.ShouldBe(ImplementDemand.Routine),
            () => settings.Model.ShouldBeEmpty(),
            () => settings.Effort.ShouldBeEmpty());
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    [InlineData("xhigh")]
    [InlineData("")]
    public void ChangeEffort_AcceptsEmptyOrALevelInTheClosedSet(string effort)
    {
        // Arrange
        ImplementTierSettings settings = ImplementTierSettings.Create(ImplementDemand.Demanding);

        // Act
        settings.ChangeEffort(effort);

        // Assert
        settings.Effort.ShouldBe(effort);
    }

    [Theory]
    [InlineData("max")]
    [InlineData("Low")]
    public void ChangeEffort_RejectsAValueOutsideTheClosedSet(string effort)
    {
        // Arrange
        ImplementTierSettings settings = ImplementTierSettings.Create(ImplementDemand.Demanding);

        // Act & Assert
        Should.Throw<ArgumentException>(() => settings.ChangeEffort(effort));
        settings.Effort.ShouldBeEmpty();
    }

    [Fact]
    public void ChangeModel_StoresTheTrimmedModel()
    {
        // Arrange
        ImplementTierSettings settings = ImplementTierSettings.Create(ImplementDemand.Routine);

        // Act
        bool changed = settings.ChangeModel("  sonnet ");

        // Assert
        changed.ShouldBeTrue();
        settings.Model.ShouldBe("sonnet");
    }

    [Fact]
    public void ChangeModel_RejectsAModelOverTheLengthCap()
    {
        // Arrange
        ImplementTierSettings settings = ImplementTierSettings.Create(ImplementDemand.Routine);

        // Act & Assert
        Should.Throw<ArgumentException>(
            () => settings.ChangeModel(new string('m', WorkerRoleSettings.MaxModelLength + 1)));
    }
}
