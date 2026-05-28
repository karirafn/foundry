using Foundry.WebApi.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Domain.WorkerRunIdTests;

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
