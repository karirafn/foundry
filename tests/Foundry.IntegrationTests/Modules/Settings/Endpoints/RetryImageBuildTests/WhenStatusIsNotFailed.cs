using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Domain;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.RetryImageBuildTests;

public sealed class WhenStatusIsNotFailed : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenStatusIsNotFailed()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private async Task SeedSettingsWithStatusAsync(Action<GlobalSettings>? configure = null)
    {
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        configure?.Invoke(settings);
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WhenStatusIsIdle_ReturnsBadRequest()
    {
        // Arrange
        await SeedSettingsWithStatusAsync();

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri("/api/settings/worker-image/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhenStatusIsBuilding_ReturnsBadRequest()
    {
        // Arrange
        await SeedSettingsWithStatusAsync(s => s.BeginImageBuild());

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri("/api/settings/worker-image/retry", UriKind.Relative),
            content: null,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
