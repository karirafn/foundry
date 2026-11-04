using Foundry.Modules.Monitoring.Domain.Entities;

using Microsoft.AspNetCore.Http;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.WebhookTests;

/// <summary>
/// The two-way port guard (ADR 0085): the public webhook port exposes only <c>/webhooks/*</c>, and the
/// main port never serves <c>/webhooks/*</c>.
/// </summary>
public sealed class WhenPortGuardApplies : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory = WebhookTestHost.CreateFactory();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData("/api/repositories")]
    [InlineData("/api/accounts")]
    [InlineData("/api/settings")]
    [InlineData("/hubs/issues/negotiate")]
    [InlineData("/")]
    public async Task WhenNonWebhookPathOnWebhookPort_Returns404(string path)
    {
        // Act
        HttpContext response = await WebhookTestHost.SendAsync(_factory, HttpMethods.Get, path, WebhookTestHost.WebhookPort);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task WhenApiPathOnMainPort_IsServed()
    {
        // Act
        HttpContext response = await WebhookTestHost.SendAsync(_factory, HttpMethods.Get, "/api/accounts", WebhookTestHost.MainPort);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task WhenValidDeliveryOnMainPort_Returns404AndIsNotProcessed()
    {
        // Arrange
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);
        string body = """{"zen":"Design for failure.","repository":{"full_name":"owner/repo"}}""";

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "ping",
            body,
            repository.WebhookSecret,
            WebhookTestHost.MainPort);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        (await WebhookTestHost.LoadRepositoryAsync(_factory, repository)).WebhookStatus
            .ShouldBe(Foundry.Modules.Monitoring.Domain.ValueObjects.WebhookStatus.Pending);
    }
}

/// <summary>Without a configured webhook port, no webhook endpoint exists on any port.</summary>
public sealed class WhenWebhookPortNotConfigured : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory = new();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Theory]
    [InlineData(WebhookTestHost.MainPort)]
    [InlineData(WebhookTestHost.WebhookPort)]
    public async Task WhenDeliveryArrives_Returns404(int port)
    {
        // Arrange
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "ping",
            """{"repository":{"full_name":"owner/repo"}}""",
            repository.WebhookSecret,
            port);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
    }
}
