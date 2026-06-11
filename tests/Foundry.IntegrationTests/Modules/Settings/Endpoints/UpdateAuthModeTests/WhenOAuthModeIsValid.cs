using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.Modules.Settings.Infrastructure;
using Foundry.Shared;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateAuthModeTests;

public sealed class WhenOAuthModeIsValid : IAsyncDisposable
{
    private static readonly DateTimeOffset CredentialsExpiry = new(2026, 12, 31, 0, 0, 0, TimeSpan.Zero);

    private static readonly OAuthCredentials StubCredentials = new(
        "access-token-value",
        "refresh-token-value",
        CredentialsExpiry,
        "pro");

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenOAuthModeIsValid()
    {
        _factory = new FoundryWebAppFactory(services =>
        {
            services.RemoveAll<IOAuthCredentialScanner>();
            services.AddScoped<IOAuthCredentialScanner>(_ => new StubOAuthCredentialScanner(
                Result<OAuthCredentials>.Ok(StubCredentials)));
        });
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
        summary.ShouldSatisfyAllConditions(
            () => summary.AuthMode.ShouldBe("OAuth"),
            () => summary.AccessTokenPresent.ShouldBeTrue(),
            () => summary.RefreshTokenPresent.ShouldBeTrue(),
            () => summary.SubscriptionType.ShouldBe("pro"),
            () => summary.ExpiresAt.ShouldBe(CredentialsExpiry));
    }

    [Fact]
    public async Task WhenOAuthScanFails_ReturnsBadRequest()
    {
        // Arrange
        await SeedDefaultSettingsAsync();

        FoundryWebAppFactory factory = new(services =>
        {
            services.RemoveAll<IOAuthCredentialScanner>();
            services.AddScoped<IOAuthCredentialScanner>(_ => new StubOAuthCredentialScanner(
                Result<OAuthCredentials>.Fail(new Error(
                    "Settings.OAuthCredentialsNotFound",
                    "No OAuth credentials file was found."))));
        });
        await using (factory)
        {
            // Need a new factory with fresh DB for the failing scan test
            using HttpClient failingClient = factory.CreateClient();

            using IServiceScope scope = factory.Services.CreateScope();
            DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            GlobalSettings settings = GlobalSettings.Create();
            dbContext.Set<GlobalSettings>().Add(settings);
            await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);

            object body = new { mode = "oauth" };

            // Act
            HttpResponseMessage response = await failingClient.PutAsJsonAsync(
                new Uri("/api/settings/auth", UriKind.Relative),
                body,
                TestContext.Current.CancellationToken);

            // Assert
            response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        }
    }

    private sealed class StubOAuthCredentialScanner(Result<OAuthCredentials> result) : IOAuthCredentialScanner
    {
        public Task<Result<OAuthCredentials>> ScanAsync(CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
