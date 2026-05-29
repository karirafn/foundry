using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.WorkerReportIdTests;

public sealed class From
{
    [Fact]
    public void WhenGivenGuid_ReturnsWorkerReportIdWithSameValue()
    {
        // Arrange
        Guid guid = Guid.NewGuid();

        // Act
        WorkerReportId id = WorkerReportId.From(guid);

        // Assert
        id.Value.ShouldBe(guid);
    }
}
