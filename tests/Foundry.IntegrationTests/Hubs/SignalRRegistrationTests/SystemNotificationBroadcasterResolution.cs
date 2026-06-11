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
}
