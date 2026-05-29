using Foundry.Shared;
using Foundry.Shared.Infrastructure;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Shared.Abstractions.ServiceCollectionExtensionsTests;

public sealed class AddIntegrationEventHandler
{
    [Fact]
    public void WhenRegistered_HandlerIsResolvableViaGenericInterface()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIntegrationEventHandler<DiTestIntegrationEvent, DiTestIntegrationEventHandler>();
        ServiceProvider provider = services.BuildServiceProvider();

        // Act
        IIntegrationEventHandler<DiTestIntegrationEvent> handler =
            provider.GetRequiredService<IIntegrationEventHandler<DiTestIntegrationEvent>>();

        // Assert
        handler.ShouldBeOfType<DiTestIntegrationEventHandler>();
    }
}
