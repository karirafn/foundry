using Foundry.Shared;
using Foundry.Shared.Infrastructure;
using Foundry.Shared.Infrastructure.Outbox;

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

    [Fact]
    public void WhenRegistered_RegistryRecordsHandlerDeclaredIdentity()
    {
        // Arrange
        ServiceCollection services = new();
        services.AddIntegrationEventHandler<DiTestIntegrationEvent, DiTestIntegrationEventHandler>();
        ServiceProvider provider = services.BuildServiceProvider();

        // Act
        HandlerDedupIdentityRegistry registry = provider.GetRequiredService<HandlerDedupIdentityRegistry>();
        string identity = registry.IdentityFor(typeof(DiTestIntegrationEventHandler));

        // Assert — the registry returns the declared attribute value, not FullName
        identity.ShouldBe("Test.DiTestIntegrationEvent");
    }
}
