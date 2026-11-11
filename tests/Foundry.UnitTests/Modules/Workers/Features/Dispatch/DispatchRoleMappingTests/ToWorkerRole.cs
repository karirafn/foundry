using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Dispatch;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Dispatch.DispatchRoleMappingTests;

public sealed class ToWorkerRole
{
    public static TheoryData<DispatchRole> AllDispatchRoles()
    {
        TheoryData<DispatchRole> data = [];

        foreach (DispatchRole role in Enum.GetValues<DispatchRole>())
        {
            data.Add(role);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AllDispatchRoles))]
    public void WhenAnyDispatchRole_MapsToADispatchableWorkerRole(DispatchRole dispatchRole)
    {
        // Act
        WorkerRole role = DispatchRoleMapping.ToWorkerRole(dispatchRole);

        // Assert
        WorkerRole.All.ShouldContain(role);
    }

    [Fact]
    public void WhenImplement_MapsToImplementWorkerRole()
    {
        // Act
        WorkerRole role = DispatchRoleMapping.ToWorkerRole(DispatchRole.Implement);

        // Assert
        role.ShouldBe(WorkerRole.Implement);
    }

    [Fact]
    public void WhenTriage_MapsToTriageWorkerRole()
    {
        // Act
        WorkerRole role = DispatchRoleMapping.ToWorkerRole(DispatchRole.Triage);

        // Assert
        role.ShouldBe(WorkerRole.Triage);
    }

    [Fact]
    public void WhenUndefinedDispatchRole_Throws()
    {
        // Arrange
        DispatchRole undefined = (DispatchRole)999;

        // Act & Assert
        Should.Throw<System.Diagnostics.UnreachableException>(() => DispatchRoleMapping.ToWorkerRole(undefined));
    }
}
