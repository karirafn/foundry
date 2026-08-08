using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.Modules.Settings.Domain.ValueObjects;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateWorkerImageConfigurationTests;

public sealed class WhenFlagsUnchanged : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenFlagsUnchanged()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedSettingsWithFlagsAsync(
        bool dotnet,
        bool angular,
        bool glab,
        bool gh,
        bool chromium,
        bool docker)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        settings.UpdateWorkerImageConfiguration(new WorkerImageConfiguration(dotnet, angular, glab, gh, chromium, docker));
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsOkWithExistingFlags()
    {
        // Arrange
        await SeedSettingsWithFlagsAsync(dotnet: true, angular: false, glab: false, gh: false, chromium: false, docker: false);
        object body = new
        {
            installDotnet = true,
            installAngular = false,
            installGlab = false,
            installGh = false,
            installChromium = false,
            installDocker = false,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/worker-image", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.InstallDotnet.ShouldBeTrue();
    }

    [Fact]
    public async Task DoesNotSetBuildingStatus()
    {
        // Arrange
        await SeedSettingsWithFlagsAsync(dotnet: true, angular: false, glab: false, gh: false, chromium: false, docker: false);
        object body = new
        {
            installDotnet = true,
            installAngular = false,
            installGlab = false,
            installGh = false,
            installChromium = false,
            installDocker = false,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/worker-image", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(
                FoundryWebAppFactory.JsonOptions,
                TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ImageBuildStatus.ShouldNotBe(ImageBuildStatus.Building);
    }
}
