using System.Net;
using System.Net.Http.Json;

using Foundry.Modules.Settings.Domain.Entities;
using Foundry.WebApi.Persistence;

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.UpdateDispatchSettingsTests;

public sealed class WhenValuesAreInvalid : IAsyncDisposable
{
    private const int BadRequestStatus = 400;
    private const int NotFoundStatus = 404;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenValuesAreInvalid()
    {
        _factory = new FoundryWebAppFactory();
        _client = _factory.CreateClient();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task WhenProbeIntervalIsBelowMin_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        object body = new { autoResumeOnUsageReset = true, probeIntervalMinutes = 4, pollIntervalSeconds = 30 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/dispatch", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("Settings.InvalidProbeInterval"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenProbeIntervalIsZero_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        object body = new { autoResumeOnUsageReset = true, probeIntervalMinutes = 0, pollIntervalSeconds = 30 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/dispatch", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("Settings.InvalidProbeInterval"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenPollIntervalIsBelowMin_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new
        {
            autoResumeOnUsageReset = true,
            probeIntervalMinutes = 30,
            pollIntervalSeconds = GlobalSettings.MinPollIntervalSeconds - 1,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/dispatch", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("Settings.InvalidPollInterval"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenPollIntervalIsAboveMax_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        await SeedDefaultSettingsAsync();
        object body = new
        {
            autoResumeOnUsageReset = true,
            probeIntervalMinutes = 30,
            pollIntervalSeconds = GlobalSettings.MaxPollIntervalSeconds + 1,
        };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/dispatch", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("Settings.InvalidPollInterval"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenSettingsDoNotExist_ReturnsNotFoundAsProblemDetails()
    {
        // Arrange — settings not seeded; probe interval valid so it passes validation
        object body = new { autoResumeOnUsageReset = true, probeIntervalMinutes = 30, pollIntervalSeconds = 30 };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/settings/dispatch", UriKind.Relative),
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

    private async Task SeedDefaultSettingsAsync()
    {
        // SettingsSeeder is a hosted service and is removed in tests — seed directly via DbContext.
        using IServiceScope scope = _factory.Services.CreateScope();
        DbContext dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();

        GlobalSettings settings = GlobalSettings.Create();
        dbContext.Set<GlobalSettings>().Add(settings);
        await dbContext.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
