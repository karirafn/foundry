using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.WorkerRunIdTests;

public sealed class From
{
    [Fact]
    public void WhenGivenGuid_ReturnsWorkerRunIdWithSameValue()
    {
        // Arrange
        Guid guid = Guid.NewGuid();

        // Act
        WorkerRunId id = WorkerRunId.From(guid);

        // Assert
        id.Value.ShouldBe(guid);
    }
}
