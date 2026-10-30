using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Contracts;
using Foundry.Modules.Settings.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateWorkerImageConfigurationTests;

public sealed class WhenImageTagAndBuildToggleChange : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenImageTagAndBuildToggleChange()
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
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ReturnsSummaryWithUpdatedImageTagAndBuildEnabled()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new
        {
            imageTag = "my-registry/foundry-worker:v2",
            buildEnabled = false,
            installDotnet = false,
            installAngular = false,
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
        summary.ShouldSatisfyAllConditions(
            () => summary.ImageTag.ShouldBe("my-registry/foundry-worker:v2"),
            () => summary.BuildEnabled.ShouldBeFalse());
    }
}
