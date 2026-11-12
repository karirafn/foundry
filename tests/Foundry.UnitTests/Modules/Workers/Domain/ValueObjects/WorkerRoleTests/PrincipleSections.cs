using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.WorkerRoleTests;

public sealed class PrincipleSections
{
    [Fact]
    public void TheImplementRole_DeclaresEverySection()
    {
        // Assert
        WorkerRole.Implement.PrincipleSections.InOrder.ShouldBe(
        [
            PrincipleSection.Testing,
            PrincipleSection.Design,
            PrincipleSection.Code,
            PrincipleSection.DomainModelling,
        ]);
    }

    [Fact]
    public void TheTriageRole_DeclaresDesignAndDomainModellingOnly()
    {
        // Assert
        WorkerRole.Triage.PrincipleSections.InOrder.ShouldBe([PrincipleSection.Design, PrincipleSection.DomainModelling]);
    }
}
