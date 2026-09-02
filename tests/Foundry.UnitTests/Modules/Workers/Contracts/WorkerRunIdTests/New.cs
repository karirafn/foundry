using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.WorkerRunIdTests;

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
