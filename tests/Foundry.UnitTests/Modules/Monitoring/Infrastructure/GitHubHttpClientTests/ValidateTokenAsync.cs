using System.Net;

using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class ValidateTokenAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    [Fact]
    public async Task WhenTokenIsUnauthorized_ReturnsAuthenticationFailed()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Unauthorized, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_bad_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        success.Value.ShouldBeOfType<TokenValidationOutcome.AuthenticationFailedOutcome>();
    }

    [Fact]
    public async Task WhenTokenIsValidAndHasRepoScope_ReturnsAuthenticatedWithNoMissingScopes()
    {
        // Arrange
        string json = """{ "login": "octocat" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        handler.ResponseHeaders["X-OAuth-Scopes"] = "repo, user";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_valid_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        TokenValidationOutcome.AuthenticatedOutcome outcome = success.Value.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>();
        outcome.ShouldSatisfyAllConditions(
            () => outcome.AccountName.ShouldBe("octocat"),
            () => outcome.MissingScopes.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenTokenLacksRepoScope_ReturnsAuthenticatedWithMissingScopes()
    {
        // Arrange
        string json = """{ "login": "octocat" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        handler.ResponseHeaders["X-OAuth-Scopes"] = "user, read:org";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_limited_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        TokenValidationOutcome.AuthenticatedOutcome outcome = success.Value.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>();
        outcome.MissingScopes.ShouldContain("repo");
    }

    [Fact]
    public async Task WhenScopesHeaderIsAbsent_ReturnsScopesUnverifiable()
    {
        // Arrange
        string json = """{ "login": "octocat" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_fine_grained_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        TokenValidationOutcome.ScopesUnverifiableOutcome outcome = success.Value.ShouldBeOfType<TokenValidationOutcome.ScopesUnverifiableOutcome>();
        outcome.AccountName.ShouldBe("octocat");
    }

    [Fact]
    public async Task WhenResponseBodyContainsUsernameButNoLogin_ReturnsProviderMismatchWithGitLab()
    {
        // Arrange
        string json = """{ "username": "gitlab_user", "id": 42 }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_some_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        TokenValidationOutcome.ProviderMismatchOutcome outcome = success.Value.ShouldBeOfType<TokenValidationOutcome.ProviderMismatchOutcome>();
        outcome.DetectedProvider.ShouldBe(ProviderTypes.GitLab);
    }

    [Fact]
    public async Task WhenResponseBodyHasEmptyLogin_ReturnsIdentityUnresolved()
    {
        // Arrange
        string json = """{ "login": "" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        handler.ResponseHeaders["X-OAuth-Scopes"] = "repo";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        success.Value.ShouldBeOfType<TokenValidationOutcome.IdentityUnresolvedOutcome>();
    }

    [Fact]
    public async Task WhenResponseBodyHasNeitherLoginNorUsername_ReturnsIdentityUnresolved()
    {
        // Arrange
        string json = """{ "id": 1 }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        handler.ResponseHeaders["X-OAuth-Scopes"] = "repo";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        success.Value.ShouldBeOfType<TokenValidationOutcome.IdentityUnresolvedOutcome>();
    }

    [Fact]
    public async Task WhenResponseBodyFailsToParse_ReturnsIdentityUnresolved()
    {
        // Arrange
        string json = "not valid json {{{";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        handler.ResponseHeaders["X-OAuth-Scopes"] = "repo";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        success.Value.ShouldBeOfType<TokenValidationOutcome.IdentityUnresolvedOutcome>();
    }

    [Fact]
    public async Task WhenApiReturnsNonSuccessNonUnauthorized_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TokenValidationOutcome>.Failure failure = result.ShouldBeOfType<Result<TokenValidationOutcome>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenBaseUrlHasInvalidScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            invalidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TokenValidationOutcome>.Failure failure = result.ShouldBeOfType<Result<TokenValidationOutcome>.Failure>();
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
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

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
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

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
    public async Task WhenNoScopesGranted_ReturnsAuthenticatedWithRepoInMissingScopes()
    {
        // Arrange
        string json = """{ "login": "octocat" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        handler.ResponseHeaders["X-OAuth-Scopes"] = string.Empty;
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_no_scopes_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        TokenValidationOutcome.AuthenticatedOutcome outcome = success.Value.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>();
        outcome.MissingScopes.ShouldBe(["repo"]);
    }
}
