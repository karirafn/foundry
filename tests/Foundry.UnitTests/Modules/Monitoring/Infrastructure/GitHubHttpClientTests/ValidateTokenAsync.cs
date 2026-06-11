using System.Net;

using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class ValidateTokenAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    [Fact]
    public async Task WhenTokenIsValidAndHasRepoScope_ReturnsValid()
    {
        // Arrange
        string json = """{ "login": "octocat" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        handler.ResponseHeaders["X-OAuth-Scopes"] = "repo, user";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_valid_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTokenLacksRepoScope_ReturnsInvalidWithMissingScope()
    {
        // Arrange
        string json = """{ "login": "octocat" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        handler.ResponseHeaders["X-OAuth-Scopes"] = "user, read:org";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_limited_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsValid.ShouldBeFalse(),
            () => success.Value.MissingScopes.ShouldContain("repo"));
    }

    [Fact]
    public async Task WhenTokenIsUnauthorized_ReturnsAuthFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Unauthorized, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_bad_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsValid.ShouldBeFalse(),
            () => success.Value.IsAuthFailure.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenApiReturnsNonSuccessNonUnauthorized_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TokenValidationResult>.Failure failure = result.ShouldBeOfType<Result<TokenValidationResult>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenBaseUrlHasInvalidScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            invalidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TokenValidationResult>.Failure failure = result.ShouldBeOfType<Result<TokenValidationResult>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        string json = """{ "login": "octocat" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        handler.ResponseHeaders["X-OAuth-Scopes"] = "repo";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        await sut.ValidateTokenAsync(ValidBaseUrl, "ghp_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/user");
    }

    [Fact]
    public async Task WhenCalled_UsesBearerTokenInAuthorizationHeader()
    {
        // Arrange
        string json = """{ "login": "octocat" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        handler.ResponseHeaders["X-OAuth-Scopes"] = "repo";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        await sut.ValidateTokenAsync(ValidBaseUrl, "ghp_my_secret_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Headers.Authorization.ShouldNotBeNull();
        request.Headers.Authorization.ShouldSatisfyAllConditions(
            () => request.Headers.Authorization.Scheme.ShouldBe("Bearer"),
            () => request.Headers.Authorization.Parameter.ShouldBe("ghp_my_secret_token"));
    }

    [Fact]
    public async Task WhenScopesHeaderIsAbsent_ReturnsValidForFineGrainedToken()
    {
        // Arrange
        string json = """{ "login": "octocat" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsValid.ShouldBeTrue(),
            () => success.Value.MissingScopes.ShouldBeEmpty());
    }
}
