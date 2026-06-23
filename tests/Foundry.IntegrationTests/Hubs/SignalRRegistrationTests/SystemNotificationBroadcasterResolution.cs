using Foundry.Shared;
using Foundry.WebApi.Hubs;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Hubs.SignalRRegistrationTests;

public sealed class SystemNotificationBroadcasterResolution : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public SystemNotificationBroadcasterResolution()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public void ResolvesToSignalRSystemNotificationBroadcaster()
    {
        // Arrange
        using IServiceScope scope = _factory.Services.CreateScope();

        // Act
        ISystemNotificationBroadcaster broadcaster = scope.ServiceProvider.GetRequiredService<ISystemNotificationBroadcaster>();

        // Assert
        broadcaster.ShouldBeOfType<SignalRSystemNotificationBroadcaster>();
    }

    [Fact]
    public void WhenRegistrationIsInspected_LifetimeIsSingleton()
    {
        // Arrange
        ServiceDescriptor? descriptor = null;

        using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            descriptor = services.LastOrDefault(
                sd => sd.ServiceType == typeof(ISystemNotificationBroadcaster));
        });

        // Act — accessing Services triggers host build, which runs ConfigureServices
        _ = factory.Services;

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }
}
