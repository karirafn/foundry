using System.Net;

using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Features.Accounts.Tokens;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class ValidateTokenAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private const string UserJson = """{ "id": 1, "username": "alice" }""";
    private const string SelfWithApiScopeJson = """{ "scopes": ["api", "read_repository"] }""";
    private const string SelfWithoutApiScopeJson = """{ "scopes": ["read_api", "read_repository"] }""";

    private static GitLabHttpClient CreateSut(FakeHandler handler) =>
        new(new HttpClient(handler), NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

    [Fact]
    public async Task WhenTokenIsUnauthorized_ReturnsAuthenticationFailed()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Unauthorized, string.Empty);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_bad_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        success.Value.ShouldBeOfType<TokenValidationOutcome.AuthenticationFailedOutcome>();
    }

    [Fact]
    public async Task WhenTokenIsValidWithApiScope_ReturnsAuthenticatedWithNoMissingScopes()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_valid_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        TokenValidationOutcome.AuthenticatedOutcome outcome =
            success.Value.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>();
        outcome.ShouldSatisfyAllConditions(
            () => outcome.AccountName.ShouldBe("alice"),
            () => outcome.MissingScopes.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenSelfScopesOmitApi_ReturnsAuthenticatedWithApiInMissingScopes()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithoutApiScopeJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_limited_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        TokenValidationOutcome.AuthenticatedOutcome outcome =
            success.Value.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>();
        outcome.MissingScopes.ShouldContain("api");
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    public async Task WhenSelfReturnsNonSuccess_ReturnsScopesUnverifiable(int statusCode)
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", (HttpStatusCode)statusCode, string.Empty);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_old_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        TokenValidationOutcome.ScopesUnverifiableOutcome outcome =
            success.Value.ShouldBeOfType<TokenValidationOutcome.ScopesUnverifiableOutcome>();
        outcome.AccountName.ShouldBe("alice");
    }

    [Fact]
    public async Task WhenUserBodyHasLoginButNoUsername_ReturnsProviderMismatchForGitHub()
    {
        // Arrange
        string githubUserJson = """{ "id": 1, "login": "octocat" }""";
        FakeHandler handler = new(HttpStatusCode.OK, githubUserJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "ghp_wrong_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        TokenValidationOutcome.ProviderMismatchOutcome outcome =
            success.Value.ShouldBeOfType<TokenValidationOutcome.ProviderMismatchOutcome>();
        outcome.DetectedProvider.ShouldBe(ProviderTypes.GitHub);
    }

    [Fact]
    public async Task WhenUserBodyHasNeitherLoginNorUsername_ReturnsIdentityUnresolved()
    {
        // Arrange
        string emptyUserJson = """{ "id": 1 }""";
        FakeHandler handler = new(HttpStatusCode.OK, emptyUserJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_no_identity_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        success.Value.ShouldBeOfType<TokenValidationOutcome.IdentityUnresolvedOutcome>();
    }

    [Fact]
    public async Task WhenUserBodyFailsToParse_ReturnsIdentityUnresolved()
    {
        // Arrange
        string invalidJson = "not-valid-json";
        FakeHandler handler = new(HttpStatusCode.OK, invalidJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_token",
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
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_token",
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
        GitLabHttpClient sut = CreateSut(handler);
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            invalidBaseUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TokenValidationOutcome>.Failure failure = result.ShouldBeOfType<Result<TokenValidationOutcome>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCalled_TargetsUserEndpointFirst()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        await sut.ValidateTokenAsync(ValidBaseUrl, "glpat_token", CancellationToken.None);

        // Assert
        handler.AllRequests.Count.ShouldBe(2);
        HttpRequestMessage firstRequest = handler.AllRequests[0];
        firstRequest.RequestUri.ShouldNotBeNull();
        firstRequest.RequestUri.AbsolutePath.ShouldBe("/api/v4/user");
    }

    [Fact]
    public async Task WhenCalled_UsesPrivateTokenHeader()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        await sut.ValidateTokenAsync(ValidBaseUrl, "glpat_my_secret_token", CancellationToken.None);

        // Assert
        HttpRequestMessage firstRequest = handler.AllRequests[0];
        firstRequest.Headers.TryGetValues("PRIVATE-TOKEN", out IEnumerable<string>? values).ShouldBeTrue();
        values.ShouldNotBeNull();
        values.FirstOrDefault().ShouldBe("glpat_my_secret_token");
    }

    [Fact]
    public async Task WhenSelfCallIsMade_TargetsPersonalAccessTokensSelfPath()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        await sut.ValidateTokenAsync(ValidBaseUrl, "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldContain("personal_access_tokens/self");
    }

    [Fact]
    public async Task WhenNoScopesGranted_MissingScopesMatchCanonicalListForGitLab()
    {
        // Arrange
        string selfNoScopesJson = """{ "scopes": [] }""";
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, selfNoScopesJson);
        GitLabHttpClient sut = CreateSut(handler);

        IReadOnlyList<string> expectedMissing = RequiredScopes.For(ProviderTypes.GitLab);

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_no_scopes_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        TokenValidationOutcome.AuthenticatedOutcome outcome =
            success.Value.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>();
        outcome.MissingScopes.ShouldBe(expectedMissing);
    }

    [Fact]
    public async Task WhenUserBodyHasEmptyUsername_ReturnsIdentityUnresolved()
    {
        // Arrange
        string userJson = """{ "id": 1, "username": "" }""";
        FakeHandler handler = new(HttpStatusCode.OK, userJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_valid_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        success.Value.ShouldBeOfType<TokenValidationOutcome.IdentityUnresolvedOutcome>();
    }

    [Fact]
    public async Task WhenSelfEndpointReturns200WithInvalidJson_ReturnsScopesUnverifiable()
    {
        // Arrange — the user endpoint succeeds; the self endpoint returns 200 but with malformed body
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, "not-valid-json");
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        Result<TokenValidationOutcome> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationOutcome>.Success success = result.ShouldBeOfType<Result<TokenValidationOutcome>.Success>();
        TokenValidationOutcome.ScopesUnverifiableOutcome outcome =
            success.Value.ShouldBeOfType<TokenValidationOutcome.ScopesUnverifiableOutcome>();
        outcome.AccountName.ShouldBe("alice");
    }
}
