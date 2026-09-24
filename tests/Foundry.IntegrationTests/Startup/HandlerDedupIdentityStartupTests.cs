using Foundry.Shared.Infrastructure.Outbox;

using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Startup;

/// <summary>
/// Verifies that all registered integration-event handlers carry a distinct
/// [IntegrationEventHandlerIdentity] attribute and that no two handlers share
/// the same identity string — proven against the runtime-populated registry,
/// so it goes red when a real handler is missing an attribute or two collide.
/// </summary>
public sealed class HandlerDedupIdentityStartupTests : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;

    // Creating the HttpClient triggers WebApplicationFactory to boot the host,
    // populating the HandlerDedupIdentityRegistry via the real AddXModule registrations.
    private readonly HttpClient _client;

    public HandlerDedupIdentityStartupTests()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public void WhenAppStarts_HandlerDedupIdentityRegistry_HasNoMissingOrCollidingIdentities()
    {
        // Arrange
        HandlerDedupIdentityRegistry registry =
            _factory.Services.GetRequiredService<HandlerDedupIdentityRegistry>();

        // Act
        Exception? thrown = Record.Exception(() => registry.Validate());

        // Assert
        thrown.ShouldBeNull("all handlers must carry a unique [IntegrationEventHandlerIdentity]");
    }
}
