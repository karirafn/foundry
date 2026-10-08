using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Mvc;

using Shouldly;

using Xunit;

namespace Foundry.IntegrationTests.Modules.Credentials.Endpoints.SubmitLoginCodeTests;

/// <summary>
/// Integration tests for POST /api/credentials/login/code — validation error paths.
/// The inline guards (empty code, too-long code) do not touch the database, so these
/// tests use a shared factory fixture. The 422 path (no active session) also requires no
/// database state beyond the default factory setup.
/// </summary>
public sealed class WhenRequestIsInvalid(FoundryWebAppFactory factory) : IClassFixture<FoundryWebAppFactory>
{
    private const int BadRequestStatus = 400;
    private const int UnprocessableEntityStatus = 422;

    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task WhenCodeIsEmpty_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        object body = new { code = string.Empty };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/credentials/login/code", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("Credentials.LoginCodeEmpty"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenCodeIsWhitespace_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        object body = new { code = "   " };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/credentials/login/code", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("Credentials.LoginCodeEmpty"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenCodeExceedsMaxLength_ReturnsBadRequestAsProblemDetails()
    {
        // Arrange
        object body = new { code = new string('x', 513) };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/credentials/login/code", UriKind.Relative),
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
            () => problem.Type.ShouldEndWith("Credentials.LoginCodeTooLong"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }

    [Fact]
    public async Task WhenNoActiveLoginSession_ReturnsUnprocessableEntityAsProblemDetails()
    {
        // Arrange — no login session started; code is valid-shaped
        object body = new { code = "valid-looking-code" };

        // Act
        HttpResponseMessage response = await _client.PostAsJsonAsync(
            new Uri("/api/credentials/login/code", UriKind.Relative),
            body,
            TestContext.Current.CancellationToken);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        ProblemDetails problem = (await response.Content
            .ReadFromJsonAsync<ProblemDetails>(TestContext.Current.CancellationToken))
            .ShouldNotBeNull();
        problem.ShouldSatisfyAllConditions(
            () => problem.Status.ShouldBe(UnprocessableEntityStatus),
            () => problem.Type.ShouldEndWith("Login.NoActiveSession"),
            () => problem.Detail.ShouldNotBeNullOrEmpty());
    }
}
