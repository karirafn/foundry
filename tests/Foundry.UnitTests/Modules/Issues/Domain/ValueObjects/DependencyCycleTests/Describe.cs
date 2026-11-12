using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DependencyCycleTests;

public sealed class Describe
{
    [Fact]
    public void WhenCycleHasThreeIssues_JoinsTheClosedPathWithArrows()
    {
        // Arrange
        DependencyCycle cycle = DependencyCycle.Create([42, 15, 31, 42])
            .ShouldBeOfType<Result<DependencyCycle>.Success>()
            .Value;

        // Act
        string description = cycle.Describe();

        // Assert
        description.ShouldBe("#42 → #15 → #31 → #42");
    }
}
