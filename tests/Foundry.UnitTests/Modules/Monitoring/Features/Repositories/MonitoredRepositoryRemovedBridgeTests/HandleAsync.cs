using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Events;
using Foundry.Modules.Monitoring.Features.Repositories;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Repositories.MonitoredRepositoryRemovedBridgeTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task PublishesOneIntegrationEventForTheSameRepositoryAndTime()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        MonitoredRepositoryRemovedBridge sut = new(dispatcher);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        DateTimeOffset removedAt = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

        // Act
        await sut.HandleAsync(
            new MonitoredRepositoryRemovedDomainEvent(repositoryId, removedAt),
            TestContext.Current.CancellationToken);

        // Assert
        dispatcher.DispatchedEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<MonitoredRepositoryRemoved>()
            .ShouldBe(new MonitoredRepositoryRemoved(repositoryId, removedAt));
    }
}
