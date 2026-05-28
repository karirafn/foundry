using Foundry.WebApi.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Domain.WorkerRunIdTests;

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
