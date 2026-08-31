using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class ExecuteGraphQlAsync
{
    private static readonly Uri GitHubComBaseUrl = new("https://api.github.com");
    private static readonly Uri GhesBaseUrl = new("https://ghes.example.com/api/v3/");

    // --- Tracer bullet: successful 200 with data envelope ---

    [Fact]
    public async Task WhenGitHubComBaseUrl_PostsToGraphQlEndpoint()
    {
        // Arrange
        string responseJson = """{"data":{"state":"OPEN"},"errors":null}""";
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsoluteUri.ShouldBe("https://api.github.com/graphql");
        request.Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task WhenGhesBaseUrl_PostsToGhesGraphQlEndpoint()
    {
        // Arrange
        string responseJson = """{"data":{"state":"OPEN"},"errors":null}""";
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GhesBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsoluteUri.ShouldBe("https://ghes.example.com/api/graphql");
    }

    [Fact]
    public async Task WhenSuccessfulResponse_ReturnsOkWithData()
    {
        // Arrange
        string responseJson = """{"data":{"state":"OPEN"},"errors":null}""";
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TestData>.Success success = result.ShouldBeOfType<Result<TestData>.Success>();
        success.Value.State.ShouldBe("OPEN");
    }

    // --- Endpoint derivation: non-https base URL ---

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsInvalidBaseUrlFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        Uri nonHttpsUrl = new("http://api.github.com");

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            nonHttpsUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TestData>.Failure failure = result.ShouldBeOfType<Result<TestData>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenBaseUrlHasFtpScheme_ReturnsInvalidBaseUrlFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        Uri ftpUrl = new("ftp://api.github.com");

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            ftpUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TestData>.Failure failure = result.ShouldBeOfType<Result<TestData>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    // --- Envelope error handling ---

    [Fact]
    public async Task When200WithNonRateLimitErrors_ReturnsGraphQlErrorFailure()
    {
        // Arrange
        string responseJson = """
            {
              "data": null,
              "errors": [
                { "message": "Could not resolve repository", "type": "NOT_FOUND" }
              ]
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TestData>.Failure failure = result.ShouldBeOfType<Result<TestData>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.GraphQlError");
        failure.Error.Message.ShouldContain("Could not resolve repository");
    }

    [Fact]
    public async Task When200WithRateLimitedErrorType_ReturnsRateLimitExhaustedFailure()
    {
        // Arrange
        string responseJson = """
            {
              "data": null,
              "errors": [
                { "message": "API rate limit exceeded", "type": "RATE_LIMITED" }
              ]
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TestData>.Failure failure = result.ShouldBeOfType<Result<TestData>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task When200WithNullDataAndNoErrors_ReturnsProviderErrorFailure()
    {
        // Arrange
        string responseJson = """{"data":null,"errors":null}""";
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TestData>.Failure failure = result.ShouldBeOfType<Result<TestData>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.ProviderError");
    }

    // --- Transport-level rate limit ---

    [Fact]
    public async Task When403WithXRateLimitRemainingZero_ReturnsRateLimitExhaustedFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TestData>.Failure failure = result.ShouldBeOfType<Result<TestData>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task When500Response_ReturnsUnexpectedStatusCodeFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TestData>.Failure failure = result.ShouldBeOfType<Result<TestData>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.UnexpectedStatusCode");
        failure.Error.Message.ShouldContain("500");
    }

    // --- Request headers ---

    [Fact]
    public async Task WhenCalled_SendsBearerTokenAndGitHubHeaders()
    {
        // Arrange
        string responseJson = """{"data":{"state":"OPEN"},"errors":null}""";
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<TestData> _ = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_mytoken",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Headers.Authorization.ShouldNotBeNull();
        request.Headers.Authorization.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe("ghp_mytoken");
        request.Headers.Contains("X-GitHub-Api-Version").ShouldBeTrue();
        request.Headers.UserAgent.ShouldContain(h => h.Product != null && h.Product.Name == "Foundry");
    }

    // --- Request body ---

    [Fact]
    public async Task WhenCalled_PostsQueryAndVariablesAsJsonBody()
    {
        // Arrange
        string responseJson = """{"data":{"state":"OPEN"},"errors":null}""";
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);
        string graphQlQuery = "query IssueState($owner: String!) { repository(owner: $owner) { id } }";
        object variables = new { owner = "my-org" };

        // Act
        Result<TestData> _ = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            graphQlQuery,
            variables,
            "ghp_token",
            CancellationToken.None);

        // Assert
        string? requestBody = handler.LastRequestBody;
        requestBody.ShouldNotBeNull();
        JsonDocument doc = JsonDocument.Parse(requestBody);
        doc.RootElement.GetProperty("query").GetString().ShouldBe(graphQlQuery);
        doc.RootElement.GetProperty("variables").GetProperty("owner").GetString().ShouldBe("my-org");
    }

    // --- Error message redaction ---

    [Fact]
    public async Task When200WithErrorsContainingToken_RedactsSecretFromMessage()
    {
        // Arrange
        string responseJson = """
            {
              "data": null,
              "errors": [
                { "message": "Access denied for token ghp_abc123secret and user", "type": "FORBIDDEN" }
              ]
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TestData>.Failure failure = result.ShouldBeOfType<Result<TestData>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.GraphQlError");
        failure.Error.Message.ShouldNotContain("ghp_abc123secret");
        failure.Error.Message.ShouldContain("***");
    }

    // Helper DTO for testing — matches a simple GraphQL response shape
    internal sealed record TestData(string? State);
}
