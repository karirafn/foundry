using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateAllowedProviderHostsTests;

public sealed class WhenHostsAreValid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenHostsAreValid()
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
    public async Task ReturnsOkWithUpdatedAllowedProviderHosts()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new { hosts = new[] { "gitlab.mycompany.com", "github.myorg.io" } };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/allowed-provider-hosts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.AllowedProviderHosts.ShouldSatisfyAllConditions(
            () => summary.AllowedProviderHosts.Count.ShouldBe(2),
            () => summary.AllowedProviderHosts.ShouldContain("gitlab.mycompany.com"),
            () => summary.AllowedProviderHosts.ShouldContain("github.myorg.io"));
    }

    [Fact]
    public async Task SubsequentGetSettingsReturnsPersistedAllowedProviderHosts()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new { hosts = new[] { "selfhosted.example.com" } };

        await _client.PutAsJsonAsync(
            new Uri("/api/settings/allowed-provider-hosts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Act
        HttpResponseMessage response = await _client.GetAsync(
            new Uri("/api/settings", UriKind.Relative),
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.AllowedProviderHosts.ShouldSatisfyAllConditions(
            () => summary.AllowedProviderHosts.Count.ShouldBe(1),
            () => summary.AllowedProviderHosts.ShouldContain("selfhosted.example.com"));
    }

    [Fact]
    public async Task NormalizesHostsToLowercase()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new { hosts = new[] { "SELFHOSTED.EXAMPLE.COM" } };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/allowed-provider-hosts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.AllowedProviderHosts.ShouldContain("selfhosted.example.com");
    }

    [Fact]
    public async Task AcceptsEmptyListToClearAllowedHosts()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object setBody = new { hosts = new[] { "selfhosted.example.com" } };
        await _client.PutAsJsonAsync(
            new Uri("/api/settings/allowed-provider-hosts", UriKind.Relative),
            setBody,
            TestContext.Current.CancellationToken);

        object clearBody = new { hosts = Array.Empty<string>() };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/allowed-provider-hosts", UriKind.Relative),
            clearBody,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.AllowedProviderHosts.ShouldBeEmpty();
    }
}
