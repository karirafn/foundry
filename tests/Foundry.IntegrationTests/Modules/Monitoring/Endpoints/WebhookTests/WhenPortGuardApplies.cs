using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

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

    [Fact]
    public async Task WhenUnknownProbeCodeOnWebhookPort_Returns404WithNoBody()
    {
        // Act
        HttpContext response = await WebhookTestHost.SendAsync(
            _factory,
            HttpMethods.Get,
            "/webhooks/probe/" + new string('a', 64),
            WebhookTestHost.WebhookPort);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        response.Response.ContentLength.GetValueOrDefault().ShouldBe(0);
    }

    [Fact]
    public async Task WhenProbeArrivesOnWebhookPort_Reaches()
    {
        // Arrange
        await using FoundryWebAppFactory factory = WebhookTestHost.CreateFactory(services =>
            services.AddHttpClient(WebhookIngressProbe.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<IServer>() is TestServer server
                    ? server.CreateHandler(context => context.Connection.LocalPort = WebhookTestHost.WebhookPort)
                    : throw new InvalidOperationException("The test host runs on a TestServer.")));
        IWebhookIngressProbe probe = factory.Services.GetRequiredService<IWebhookIngressProbe>();

        // Act
        WebhookProbeResult result = await probe.VerifyAsync("https://hooks.example.com", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<WebhookProbeResult.Reached>();
    }

    [Fact]
    public async Task WhenProbeArrivesOnMainPort_FailsAsOtherInstance()
    {
        // Arrange
        await using FoundryWebAppFactory factory = WebhookTestHost.CreateFactory(services =>
            services.AddHttpClient(WebhookIngressProbe.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<IServer>() is TestServer server
                    ? server.CreateHandler(context => context.Connection.LocalPort = WebhookTestHost.MainPort)
                    : throw new InvalidOperationException("The test host runs on a TestServer.")));
        IWebhookIngressProbe probe = factory.Services.GetRequiredService<IWebhookIngressProbe>();

        // Act
        WebhookProbeResult result = await probe.VerifyAsync("https://hooks.example.com", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<WebhookProbeResult.Unreachable>().Failure.ShouldBeOfType<WebhookProbeFailure.OtherInstance>();
    }
}

/// <summary>Without <c>Webhooks:Port</c> the webhook port is still bound, on the default port (ADR 0116).</summary>
public sealed class WhenWebhookPortNotConfigured : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory = new();

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public void ProbePort_IsTheDefault()
    {
        // Act
        int port = _factory.Services.GetRequiredService<IWebhookIngressProbe>().Port;

        // Assert
        port.ShouldBe(5099);
    }

    [Fact]
    public async Task WhenDeliveryArrivesOnDefaultPort_IsServed()
    {
        // Arrange
        MonitoredRepository repository = await WebhookTestHost.SeedRepositoryAsync(_factory, registered: true);

        // Act
        HttpContext response = await WebhookTestHost.DeliverAsync(
            _factory,
            repository.WebhookHookId,
            "ping",
            """{"zen":"Design for failure.","repository":{"full_name":"owner/repo"}}""",
            repository.WebhookSecret,
            WebhookTestHost.WebhookPort);

        // Assert
        response.Response.StatusCode.ShouldBe(StatusCodes.Status202Accepted);
    }
}
