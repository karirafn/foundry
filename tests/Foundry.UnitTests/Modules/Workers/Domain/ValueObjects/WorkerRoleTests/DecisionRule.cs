using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.WorkerRoleTests;

public sealed class DecisionRule
{
    public static TheoryData<string> DispatchableRoleNames => [.. WorkerRole.All.Select(role => role.Name)];

    [Theory]
    [MemberData(nameof(DispatchableRoleNames))]
    public void EveryDispatchableRole_CarriesTheDecisionRule(string roleName)
    {
        // Assert
        WorkerRole.FromStored(roleName).CarriesDecisionRule.ShouldBeTrue();
    }

    [Fact]
    public void WhenTheNameIsNoLongerARole_DoesNotCarryTheDecisionRule()
    {
        // Act
        WorkerRole role = WorkerRole.FromStored("retired-role");

        // Assert
        role.CarriesDecisionRule.ShouldBeFalse();
    }
}
