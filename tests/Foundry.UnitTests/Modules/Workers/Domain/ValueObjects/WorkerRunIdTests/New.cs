using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.WorkerRunIdTests;

public sealed class New
{
    [Fact]
    public void WhenCalledTwice_ProducesDistinctIds()
    {
        // Arrange

        // Act
        WorkerRunId a = WorkerRunId.New();
        WorkerRunId b = WorkerRunId.New();

        // Assert
        a.ShouldNotBe(b);
    }
}
