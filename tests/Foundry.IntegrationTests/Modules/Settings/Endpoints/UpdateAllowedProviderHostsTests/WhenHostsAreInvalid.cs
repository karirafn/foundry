using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateAllowedProviderHostsTests;

public sealed class WhenHostsAreInvalid : IAsyncDisposable
{
    private const int BadRequestStatus = 400;
    private const int NotFoundStatus = 404;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenHostsAreInvalid()
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
    public async Task WhenHostCarriesScheme_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        const string InvalidHost = "https://git.example.com";
        object body = new { hosts = new[] { InvalidHost } };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/allowed-provider-hosts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.ShouldSatisfyAllConditions(
            () => problem.Status.ShouldBe(BadRequestStatus),
            () => problem.Type.ShouldEndWith("Settings.InvalidProviderHost"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenHostCarriesPort_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        const string InvalidHost = "git.example.com:8443";
        object body = new { hosts = new[] { InvalidHost } };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/allowed-provider-hosts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.ShouldSatisfyAllConditions(
            () => problem.Status.ShouldBe(BadRequestStatus),
            () => problem.Type.ShouldEndWith("Settings.InvalidProviderHost"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenHostHasTrailingDot_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        const string InvalidHost = "git.example.com.";
        object body = new { hosts = new[] { InvalidHost } };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/allowed-provider-hosts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.ShouldSatisfyAllConditions(
            () => problem.Status.ShouldBe(BadRequestStatus),
            () => problem.Type.ShouldEndWith("Settings.InvalidProviderHost"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenSettingsDoNotExist_ReturnsNotFoundAsProblemDetails()
    {
        // Arrange — no settings seeded; SettingsSeeder is a hosted service and is removed in tests
        object body = new { hosts = new[] { "git.example.com" } };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/allowed-provider-hosts", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.ShouldSatisfyAllConditions(
            () => problem.Status.ShouldBe(NotFoundStatus),
            () => problem.Type.ShouldEndWith("Settings.NotFound"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }
}
