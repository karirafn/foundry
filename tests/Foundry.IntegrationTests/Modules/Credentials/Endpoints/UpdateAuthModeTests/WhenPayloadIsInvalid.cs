using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.UpdateAuthModeTests;

public sealed class WhenPayloadIsInvalid(FoundryWebAppFactory factory) : IClassFixture<FoundryWebAppFactory>
{
    private const int BadRequestStatus = 400;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task WhenModeIsUnknown_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        object body = new { mode = "unknown_mode" };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/credentials/auth", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("Credentials.InvalidAuthMode"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenApiKeyModeAndKeyIsMissing_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        object body = new { mode = "api_key" };

        // Act
        HttpResponseMessage response = await _client.PutAsJsonAsync(
            new Uri("/api/credentials/auth", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("Credentials.InvalidAuthMode"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }
}
