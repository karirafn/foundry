using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateAuthModeTests;

public sealed class WhenOAuthModeIsValid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenOAuthModeIsValid()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedDefaultSettingsAsync()
    {
        // SettingsSeeder is a hosted service and is removed in tests — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsOkWithOAuthMode()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new { mode = "oauth" };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SubsequentGetShowsOAuthMode()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new { mode = "oauth" };

        await _client.PutAsJsonAsync(
            new Uri("/api/settings/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.AuthMode.ShouldBe("OAuth");
    }
}
