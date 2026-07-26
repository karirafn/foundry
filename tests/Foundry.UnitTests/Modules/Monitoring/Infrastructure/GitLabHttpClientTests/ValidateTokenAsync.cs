using System.Net;

using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class ValidateTokenAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private const string UserJson = """{ "id": 1, "username": "alice" }""";
    private const string SelfWithApiScopeJson = """{ "scopes": ["api", "read_repository"] }""";
    private const string SelfWithoutApiScopeJson = """{ "scopes": ["read_api", "read_repository"] }""";

    [Fact]
    public async Task WhenTokenIsValid_ReturnsValid()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_valid_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsValid.ShouldBeTrue(),
            () => success.Value.IsAuthFailure.ShouldBeFalse(),
            () => success.Value.MissingScopes.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenSelfReturnsApiScope_ScopesVerifiedIsTrueAndMissingScopesIsEmpty()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_valid_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsValid.ShouldBeTrue(),
            () => success.Value.ScopesVerified.ShouldBeTrue(),
            () => success.Value.MissingScopes.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenSelfScopesOmitApi_ReturnsInvalidWithApiInMissingScopes()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithoutApiScopeJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_limited_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsValid.ShouldBeFalse(),
            () => success.Value.ScopesVerified.ShouldBeTrue(),
            () => success.Value.MissingScopes.ShouldContain("api"));
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
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_old_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsValid.ShouldBeFalse(),
            () => success.Value.ScopesVerified.ShouldBeFalse(),
            () => success.Value.MissingScopes.ShouldBeEmpty(),
            () => success.Value.AccountName.ShouldBe("alice"));
    }

    [Fact]
    public async Task WhenSelfCallIsMade_TargetsPersonalAccessTokensSelfPath()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.ValidateTokenAsync(ValidBaseUrl, "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldContain("personal_access_tokens/self");
    }

    [Fact]
    public async Task WhenSelfCallIsMade_UsesPrivateTokenHeader()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.ValidateTokenAsync(ValidBaseUrl, "glpat_my_secret_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Headers.TryGetValues("PRIVATE-TOKEN", out IEnumerable<string>? values).ShouldBeTrue();
        values.ShouldNotBeNull();
        values.FirstOrDefault().ShouldBe("glpat_my_secret_token");
    }

    [Fact]
    public async Task WhenNoScopesGranted_MissingScopesMatchCanonicalListForGitLab()
    {
        // Arrange
        string selfNoScopesJson = """{ "scopes": [] }""";
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, selfNoScopesJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        IReadOnlyList<string> expectedMissing = RequiredScopes.For(ProviderTypes.GitLab);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_no_scopes_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.MissingScopes.ShouldBe(expectedMissing);
    }

    [Fact]
    public async Task WhenTokenIsUnauthorized_ReturnsAuthFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Unauthorized, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_bad_token",
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
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_token",
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
        GitLabHttpClient sut = new(httpClient);
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            invalidBaseUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<TokenValidationResult>.Failure failure = result.ShouldBeOfType<Result<TokenValidationResult>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.ValidateTokenAsync(ValidBaseUrl, "glpat_token", CancellationToken.None);

        // Assert — first request goes to /user
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
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.ValidateTokenAsync(ValidBaseUrl, "glpat_my_secret_token", CancellationToken.None);

        // Assert — first request (user endpoint) carries PRIVATE-TOKEN
        HttpRequestMessage firstRequest = handler.AllRequests[0];
        firstRequest.Headers.TryGetValues("PRIVATE-TOKEN", out IEnumerable<string>? values).ShouldBeTrue();
        values.ShouldNotBeNull();
        values.FirstOrDefault().ShouldBe("glpat_my_secret_token");
    }

    [Fact]
    public async Task WhenResponseBodyContainsUsername_AccountNameIsResolved()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_valid_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.AccountName.ShouldBe("alice");
    }

    [Fact]
    public async Task WhenResponseBodyHasEmptyUsername_AccountNameIsNull()
    {
        // Arrange
        string userJson = """{ "id": 1, "username": "" }""";
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, userJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_valid_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.AccountName.ShouldBeNull();
    }

    [Fact]
    public async Task WhenResponseBodyHasAbsentUsername_AccountNameIsNull()
    {
        // Arrange
        string userJson = """{ "id": 1 }""";
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, userJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, SelfWithApiScopeJson);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_valid_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.AccountName.ShouldBeNull();
    }

    [Fact]
    public async Task WhenAuthFailure_AccountNameIsNull()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Unauthorized, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<TokenValidationResult> result = await sut.ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_bad_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TokenValidationResult>.Success success = result.ShouldBeOfType<Result<TokenValidationResult>.Success>();
        success.Value.AccountName.ShouldBeNull();
    }
}
