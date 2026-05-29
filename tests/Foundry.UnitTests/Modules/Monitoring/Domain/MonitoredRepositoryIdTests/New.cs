using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryIdTests;

public sealed class New
{
    [Fact]
    public void WhenCalledTwice_ProducesDistinctIds()
    {
        // Arrange

        // Act
        MonitoredRepositoryId a = MonitoredRepositoryId.New();
        MonitoredRepositoryId b = MonitoredRepositoryId.New();

        // Assert
        a.ShouldNotBe(b);
    }
}
