using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.EligibilityViolationInfoTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_StoresRuleAndDescription()
    {
        // Arrange
        const string rule = "branch-protection:allow-direct-pushes";
        const string description = "Direct pushes are allowed.";

        // Act
        EligibilityViolationInfo info = new(rule, description);

        // Assert
        info.ShouldSatisfyAllConditions(
            () => info.Rule.ShouldBe(rule),
            () => info.Description.ShouldBe(description));
    }
}
