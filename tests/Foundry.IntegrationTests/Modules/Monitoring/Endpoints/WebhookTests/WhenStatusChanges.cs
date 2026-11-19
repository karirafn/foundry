using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Testing;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.WebhookTests;

/// <summary>Committed webhook status changes are broadcast to the dashboard.</summary>
public sealed class WhenStatusChanges : IAsyncDisposable
{
    private readonly RecordingRepositoryWebhookBroadcaster _broadcaster = new();
    private readonly FoundryWebAppFactory _factory;

    public WhenStatusChanges()
    {
        _factory = WebhookTestHost.CreateFactory(services =>
            services.AddSingleton<IRepositoryWebhookBroadcaster>(_broadcaster));
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task VerifiedPingWhilePending_BroadcastsTheRepository()
    {
        // Arrange
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "ping",
            """{"zen":"x","repository":{"full_name":"owner/repo"}}""",
            repository.WebhookSecret);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
        _broadcaster.Changed.ShouldBe([repository.Id]);
    }
}
