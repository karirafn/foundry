using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Monitoring.Endpoints.ValidateTokenTests;

public sealed class WhenRequestIsInvalid : IAsyncDisposable
{
    private const int BadRequestStatus = 400;

    private readonly FoundryWebAppFactory _factory;
    private readonly HttpClient _client;

    public WhenRequestIsInvalid()
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
    public async Task WhenBaseUrlIsNotAbsolute_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        object body = new { token = "ghp_token", baseUrl = "not-a-url", providerType = "github" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("BaseUrl.Invalid"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenBaseUrlIsNotHttps_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        object body = new { token = "ghp_token", baseUrl = "http://github.com", providerType = "github" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("BaseUrl.Invalid"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenProviderTypeIsUnknown_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        object body = new { token = "some_token", baseUrl = "https://github.com", providerType = "bitbucket" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("ProviderType.Unknown"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenProviderTypeIsOmitted_ReturnsBadRequest()
    {
        // Arrange
        object body = new { token = "some_token", baseUrl = "https://github.com" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/accounts/validate-token", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        // providerType is [JsonRequired] — model-binding rejection yields a framework 400 (no Error object).
        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }
}
