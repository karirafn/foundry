using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.WorkerRoleTests;

public sealed class Capability
{
    [Fact]
    public void TheImplementRole_RunsWithFullCapability()
    {
        // Assert
        WorkerRole.Implement.Capability.ShouldBe(WorkerCapability.Full);
    }

    [Fact]
    public void TheTriageRole_RunsRestricted()
    {
        // Assert
        WorkerRole.Triage.Capability.ShouldBe(WorkerCapability.Restricted);
    }

    [Fact]
    public void EveryDispatchableRole_ListsTriageBeforeImplement()
    {
        // Assert
        WorkerRole.All.ShouldBe([WorkerRole.Triage, WorkerRole.Implement]);
    }

    [Theory]
    [InlineData(WorkerCapability.Restricted, "restricted")]
    [InlineData(WorkerCapability.Full, "full")]
    public void EveryCapability_HasALowerCaseName(WorkerCapability capability, string expected)
    {
        // Assert
        capability.ToName().ShouldBe(expected);
    }
}
