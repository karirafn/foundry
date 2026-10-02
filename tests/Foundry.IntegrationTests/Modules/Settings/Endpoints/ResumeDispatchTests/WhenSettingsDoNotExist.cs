using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Settings.Endpoints.ResumeDispatchTests;

public sealed class WhenSettingsDoNotExist : IAsyncDisposable
{
    private const int NotFoundStatus = 404;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenSettingsDoNotExist()
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
    public async Task ReturnsNotFoundAsProblemDetails()
    {
        // Arrange — no settings seeded; SettingsSeeder is a hosted service and is removed in tests

        // Act
        HttpResponseMessage response = await _client.PostAsync(
            new Uri("/api/settings/dispatch/resume", UriKind.Relative),
            content: null,
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
