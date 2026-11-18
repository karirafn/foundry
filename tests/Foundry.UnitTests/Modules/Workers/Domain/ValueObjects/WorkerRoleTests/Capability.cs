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
    public void TheReviewRole_RunsRestricted()
    {
        // Assert
        WorkerRole.Review.Capability.ShouldBe(WorkerCapability.Restricted);
    }

    [Fact]
    public void TheReviewRole_IsNamedReview()
    {
        // Assert
        WorkerRole.Review.Name.ShouldBe("review");
    }

    [Fact]
    public void EveryDispatchableRole_ListsTriageThenImplementThenReview()
    {
        // Assert
        WorkerRole.All.ShouldBe([WorkerRole.Triage, WorkerRole.Implement, WorkerRole.Review]);
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
