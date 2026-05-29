using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.MonitoredRepositoryIdTests;

public sealed class From
{
    [Fact]
    public void WhenGivenGuid_ReturnsMonitoredRepositoryIdWithSameValue()
    {
        // Arrange
        Guid guid = Guid.NewGuid();

        // Act
        MonitoredRepositoryId id = MonitoredRepositoryId.From(guid);

        // Assert
        id.Value.ShouldBe(guid);
    }
}
