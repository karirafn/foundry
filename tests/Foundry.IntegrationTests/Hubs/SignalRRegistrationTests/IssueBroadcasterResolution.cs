using Foundry.Modules.Issues.Contracts;
using Foundry.WebApi.Hubs;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Hubs.SignalRRegistrationTests;

public sealed class IssueBroadcasterResolution : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public IssueBroadcasterResolution()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public void ResolvesToSignalRIssueBroadcaster()
    {
        // Arrange
        using IServiceScope scope = _factory.Services.CreateScope();

        // Act
        IIssueBroadcaster broadcaster = scope.ServiceProvider.GetRequiredService<IIssueBroadcaster>();

        // Assert
        broadcaster.ShouldBeOfType<SignalRIssueBroadcaster>();
    }
}
