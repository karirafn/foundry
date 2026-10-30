using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateWorkerImageConfigurationTests;

public sealed class WhenImageTagInvalid : IAsyncDisposable
{
    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenImageTagInvalid()
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
    public async Task WhenEmptyImageTag_ReturnsBadRequest()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new
        {
            imageTag = "",
            buildEnabled = true,
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
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Type.ShouldEndWith("Settings.EmptyImageTag");
    }

    [Fact]
    public async Task WhenLatestImageTag_ReturnsBadRequest()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new
        {
            imageTag = ":latest",
            buildEnabled = true,
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
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.Type.ShouldEndWith("Settings.LatestImageTag");
    }
}
