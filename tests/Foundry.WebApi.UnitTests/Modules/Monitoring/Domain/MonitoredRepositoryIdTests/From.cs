using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryIdTests;

public sealed class From
{
    [Fact]
    public void WhenGivenGuid_ReturnsIdWithSameValue()
    {
        // Arrange
        Guid guid = Guid.NewGuid();

        // Act
        MonitoredRepositoryId id = MonitoredRepositoryId.From(guid);

        // Assert
        id.Value.ShouldBe(guid);
    }
}
