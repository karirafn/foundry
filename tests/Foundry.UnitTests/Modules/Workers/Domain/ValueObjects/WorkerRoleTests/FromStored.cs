using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.WorkerRoleTests;

public sealed class FromStored
{
    [Fact]
    public void WhenTheNameIsAKnownRole_ReturnsThatRole()
    {
        // Act
        WorkerRole role = WorkerRole.FromStored("implement");

        // Assert
        role.ShouldBeSameAs(WorkerRole.Implement);
    }

    [Fact]
    public void WhenTheNameIsNoLongerARole_KeepsTheRecordedName()
    {
        // Act
        WorkerRole role = WorkerRole.FromStored("retired-role");

        // Assert
        role.ShouldSatisfyAllConditions(
            () => role.Name.ShouldBe("retired-role"),
            () => WorkerRole.All.ShouldNotContain(role));
    }

    [Fact]
    public void WhenTheNameIsNoLongerARole_IsRestricted()
    {
        // Act
        WorkerRole role = WorkerRole.FromStored("retired-role");

        // Assert
        role.Capability.ShouldBe(WorkerCapability.Restricted);
    }

    [Fact]
    public void TheTriageRole_IsKnownByName()
    {
        // Act
        WorkerRole role = WorkerRole.FromStored("triage");

        // Assert
        role.ShouldBeSameAs(WorkerRole.Triage);
    }

    [Fact]
    public void TheSharedPluginFolder_IsNeverARole()
    {
        // Assert
        WorkerRole.All.ShouldNotContain(role => role.Name == WorkerRole.SharedFolder);
    }
}
