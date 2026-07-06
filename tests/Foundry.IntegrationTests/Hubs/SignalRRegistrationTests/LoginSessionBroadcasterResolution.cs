using Foundry.Modules.Credentials.Contracts;
using Foundry.WebApi.Hubs;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Hubs.SignalRRegistrationTests;

public sealed class LoginSessionBroadcasterResolution : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    public LoginSessionBroadcasterResolution()
    {
        _factory = new FoundryWebAppFactory();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public void ResolvesToSignalRLoginSessionBroadcaster()
    {
        // Arrange
        using IServiceScope scope = _factory.Services.CreateScope();

        // Act
        ILoginSessionBroadcaster broadcaster = scope.ServiceProvider.GetRequiredService<ILoginSessionBroadcaster>();

        // Assert
        broadcaster.ShouldBeOfType<SignalRLoginSessionBroadcaster>();
    }

    [Fact]
    public void WhenRegistrationIsInspected_LifetimeIsSingleton()
    {
        // Arrange
        ServiceDescriptor? descriptor = null;

        using FoundryWebAppFactory factory = FoundryWebAppFactory.WithOverrides(services =>
        {
            descriptor = services.LastOrDefault(
                sd => sd.ServiceType == typeof(ILoginSessionBroadcaster));
        });

        // Act — accessing Services triggers host build, which runs ConfigureServices
        _ = factory.Services;

        // Assert
        descriptor.ShouldNotBeNull();
        descriptor.Lifetime.ShouldBe(ServiceLifetime.Singleton);
    }
}
