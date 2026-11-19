using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.IntegrationTests.Shared;
using Foundry.Shared.Infrastructure.Outbox;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateWebhookSettingsTests;

/// <summary>A changed URL whose self-probe fails is rejected and nothing is saved (ADR 0116).</summary>
public sealed class WhenProbeFails : IAsyncDisposable
{
    private const string StoredUrl = "https://hooks.example.com";
    private const int WebhookPort = 6123;

    private readonly StubWebhookIngressProbe _probe = new(
        WebhookPort,
        new WebhookProbeResult.Unreachable(new WebhookProbeFailure.Timeout()));

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenProbeFails()
    {
        _factory = FoundryWebAppFactory.WithOverrides(services => services.AddSingleton<IWebhookIngressProbe>(_probe));
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenNewUrlTimesOut_Returns400UnreachableAndKeepsStoredUrl()
    {
        // Arrange
        using (IServiceScope seedScope = _factory.Services.CreateScope())
        {
            DbContext seedContext = seedScope.ServiceProvider.GetRequiredService<DbContext>();
            GlobalSettings seeded = GlobalSettings.Create();
            seeded.UpdateWebhookSettings(StoredUrl, 15);
            seedContext.Set<GlobalSettings>().Add(seeded);
            await seedContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/webhooks", UriKind.Relative),
            new { webhookPublicBaseUrl = "https://new.example.com", backstopIntervalMinutes = 30 },
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ProblemDetails problem = (await response.Content.ReadFromJsonAsync<ProblemDetails>(
            TestContext.Current.CancellationToken)).ShouldNotBeNull();
        problem.Type.ShouldEndWith("/Settings.WebhookPublicBaseUrlUnreachable");
        problem.Detail.ShouldBe(
            "new.example.com didn't answer within 10 seconds. Check that the tunnel or proxy is running and forwards to port 6123 on the Foundry host. If the proxy sits behind the same router as Foundry, the router may not route requests back to its own public address.");

        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
        GlobalSettings settings = await dbContext.Set<GlobalSettings>().AsNoTracking().SingleAsync(TestContext.Current.CancellationToken);
        settings.WebhookPublicBaseUrl?.Value.ShouldBe(StoredUrl);
        settings.BackstopIntervalMinutes.ShouldBe(15);
        (await dbContext.Set<OutboxMessage>().AsNoTracking().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }
}
