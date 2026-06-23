using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateWorkerImageConfigurationTests;

public sealed class WhenFlagsChange : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenFlagsChange()
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
    public async Task ReturnsOkWithUpdatedFlags()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new
        {
            installDotnet = true,
            installAngular = false,
            installGlab = true,
            installGh = false,
            installChromium = true,
            installDocker = true,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/worker-image", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        GlobalSettingsSummary? summary = await response.Content
            .ReadFromJsonAsync<GlobalSettingsSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ShouldSatisfyAllConditions(
            () => summary.InstallDotnet.ShouldBeTrue(),
            () => summary.InstallAngular.ShouldBeFalse(),
            () => summary.InstallGlab.ShouldBeTrue(),
            () => summary.InstallGh.ShouldBeFalse(),
            () => summary.InstallChromium.ShouldBeTrue(),
            () => summary.InstallDocker.ShouldBeTrue());
    }

    [Fact]
    public async Task ReturnsIdleStatusBeforeBackgroundConsumerRuns()
    {
        // Arrange — Building status is set by WorkerImageRebuildService (background), not the handler
        await SeedDefaultSettingsAsync();
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
            .ReadFromJsonAsync<GlobalSettingsSummary>(TestContext.Current.CancellationToken);
        summary.ShouldNotBeNull();
        summary.ImageBuildStatus.ShouldBe(ImageBuildStatus.Idle);
    }
}
