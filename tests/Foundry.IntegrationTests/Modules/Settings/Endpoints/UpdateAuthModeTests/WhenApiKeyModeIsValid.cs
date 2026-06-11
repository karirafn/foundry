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

public sealed class WhenApiKeyModeIsValid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenApiKeyModeIsValid()
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
    public async Task ReturnsOkWithApiKeyMode()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new { mode = "api_key", apiKey = "sk-ant-test-key" };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SubsequentGetReturnsApiKeyMode()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new { mode = "api_key", apiKey = "sk-ant-test-key" };

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
        summary.AuthMode.ShouldBe("ApiKey");
    }

    [Fact]
    public async Task StoredValueIsDifferentFromPlaintext()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        const string PlaintextKey = "sk-ant-test-key";
        object body = new { mode = "api_key", apiKey = PlaintextKey };

        await _client.PutAsJsonAsync(
            new Uri("/api/settings/auth", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Act — read the raw auth_mode column directly from the database
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        // Use raw SQL to read the encrypted column without EF decryption
        string rawAuthMode = await dbContext.Database
            .SqlQueryRaw<string>("SELECT auth_mode AS Value FROM global_settings LIMIT 1")
            .FirstAsync(TestContext.Current.CancellationToken);

        // Assert — the stored bytes must not contain the plaintext API key
        rawAuthMode.ShouldNotContain(PlaintextKey);
    }
}
