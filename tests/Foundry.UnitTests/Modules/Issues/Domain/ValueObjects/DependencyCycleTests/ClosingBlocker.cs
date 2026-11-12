using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects.DependencyCycleTests;

public sealed class ClosingBlocker
{
    [Fact]
    public void WhenCycleIsClosed_IsTheSecondIssueOnThePath()
    {
        // Arrange
        DependencyCycle cycle = DependencyCycle.Create([42, 15, 31, 42])
            .ShouldBeOfType<Result<DependencyCycle>.Success>()
            .Value;

        // Act
        int closingBlocker = cycle.ClosingBlocker;

        // Assert
        closingBlocker.ShouldBe(cycle.Path[1]);
    }
}
