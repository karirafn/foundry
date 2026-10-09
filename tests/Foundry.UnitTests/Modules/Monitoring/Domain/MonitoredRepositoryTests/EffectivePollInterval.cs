using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.MonitoredRepositoryTests;

public sealed class EffectivePollInterval
{
    [Fact]
    public void WhenPollIntervalIsSet_ReturnsOwnInterval()
    {
        // Arrange
        TimeSpan ownInterval = TimeSpan.FromMinutes(15);
        TimeSpan defaultInterval = TimeSpan.FromMinutes(5);
        MonitoredRepository repository = new MonitoredRepositoryBuilder()
            .WithPollInterval(ownInterval)
            .Build();

        // Act
        TimeSpan result = repository.EffectivePollInterval(defaultInterval);

        // Assert
        result.ShouldBe(ownInterval);
    }

    [Fact]
    public void WhenPollIntervalIsNull_ReturnsDefaultInterval()
    {
        // Arrange
        TimeSpan defaultInterval = TimeSpan.FromMinutes(5);
        MonitoredRepository repository = new MonitoredRepositoryBuilder()
            .WithPollInterval(null)
            .Build();

        // Act
        TimeSpan result = repository.EffectivePollInterval(defaultInterval);

        // Assert
        result.ShouldBe(defaultInterval);
    }
}
