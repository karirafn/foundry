using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Accounts;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
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
    // Shape captured from gitlab.com on 2026-11-24 (/personal_access_tokens/self for a fine-grained token).
    private const string FineGrainedSelfJson = """
        {
          "scopes": ["granular"],
          "expires_at": null,
          "granular_scopes": [
            { "access": "selected_memberships", "permissions": ["read_job", "run_job"], "project_id": 42, "group_id": null },
            { "access": "user", "permissions": ["read_personal_access_token", "read_user"], "project_id": null, "group_id": null }
          ]
        }
        """;

    private static GitLabHttpClient CreateSut(FakeHandler handler) =>
        new(new HttpClient(handler), NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

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
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, FineGrainedSelfJson);
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
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, FineGrainedSelfJson);
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
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, FineGrainedSelfJson);
        GitLabHttpClient sut = CreateSut(handler);

        // Act
        await sut.ValidateTokenAsync(ValidBaseUrl, "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldContain("personal_access_tokens/self");
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

    private static async Task<TokenValidationOutcome> ValidateWithSelfAsync(HttpStatusCode status, string selfBody)
    {
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", status, selfBody);
        Result<TokenValidationOutcome> result = await CreateSut(handler).ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_token",
            CancellationToken.None);
        return result.ValueOrThrow();
    }

    [Fact]
    public async Task WhenSelfReportsGranularScopes_ReturnsTheGrantAsGitLabReportedIt()
    {
        // Arrange

        // Act
        TokenValidationOutcome outcome = await ValidateWithSelfAsync(HttpStatusCode.OK, FineGrainedSelfJson);

        // Assert
        TokenValidationOutcome.GrantReadOutcome grantRead = outcome.ShouldBeOfType<TokenValidationOutcome.GrantReadOutcome>();
        grantRead.ShouldSatisfyAllConditions(
            () => grantRead.AccountName.ShouldBe("alice"),
            () => grantRead.Grant.Entries.ShouldBe(
            [
                new GitLabTokenGrantEntry("selected_memberships", ["read_job", "run_job"], 42, null),
                new GitLabTokenGrantEntry("user", ["read_personal_access_token", "read_user"], null, null),
            ]));
    }

    [Fact]
    public async Task WhenSelfIsRefusedForInsufficientGranularScope_ReturnsAuthenticatedMissingPersonalAccessTokenRead()
    {
        // Arrange
        // Body captured from gitlab.com on 2026-11-24 for a fine-grained token without read_personal_access_token.
        const string body = """{"error":"insufficient_granular_scope"}""";

        // Act
        TokenValidationOutcome outcome = await ValidateWithSelfAsync(HttpStatusCode.Forbidden, body);

        // Assert
        TokenValidationOutcome.AuthenticatedOutcome authenticated =
            outcome.ShouldBeOfType<TokenValidationOutcome.AuthenticatedOutcome>();
        authenticated.MissingScopes.ShouldBe(["Personal Access Token (System Access): Read"]);
    }

    [Fact]
    public async Task WhenSelfHasNoGranularScopesAndIsNotGranular_ReturnsWrongKindGitLabLegacy()
    {
        // Arrange

        // Act
        TokenValidationOutcome outcome = await ValidateWithSelfAsync(
            HttpStatusCode.OK,
            """{ "scopes": ["api"], "expires_at": null }""");

        // Assert
        outcome.ShouldBeOfType<TokenValidationOutcome.WrongTokenKindOutcome>().WrongKind
            .ShouldBe(WrongTokenKind.GitLabLegacy);
    }

    [Fact]
    public async Task WhenSelfIsGranularButReportsNoGranularScopes_ReturnsWrongKindGitLabVersion()
    {
        // Arrange

        // Act
        TokenValidationOutcome outcome = await ValidateWithSelfAsync(
            HttpStatusCode.OK,
            """{ "scopes": ["granular"], "expires_at": null }""");

        // Assert
        outcome.ShouldBeOfType<TokenValidationOutcome.WrongTokenKindOutcome>().WrongKind
            .ShouldBe(WrongTokenKind.GitLabVersion);
    }

    [Theory]
    [InlineData(401, "")]
    [InlineData(403, "{\"message\":\"403 Forbidden\"}")]
    [InlineData(404, "")]
    public async Task WhenSelfIsRefusedOtherwise_ReturnsWrongKindGitLabResourceToken(int statusCode, string body)
    {
        // Arrange

        // Act
        TokenValidationOutcome outcome = await ValidateWithSelfAsync((HttpStatusCode)statusCode, body);

        // Assert
        TokenValidationOutcome.WrongTokenKindOutcome wrongKind =
            outcome.ShouldBeOfType<TokenValidationOutcome.WrongTokenKindOutcome>();
        wrongKind.ShouldSatisfyAllConditions(
            () => wrongKind.WrongKind.ShouldBe(WrongTokenKind.GitLabResourceToken),
            () => wrongKind.AccountName.ShouldBe("alice"));
    }

    [Theory]
    [InlineData(429)]
    [InlineData(502)]
    public async Task WhenSelfIsThrottledOrFailing_ReturnsFailure(int statusCode)
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", (HttpStatusCode)statusCode, string.Empty);

        // Act
        Result<TokenValidationOutcome> result = await CreateSut(handler).ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenSelfEndpointReturns200WithInvalidJson_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new FakeHandler(HttpStatusCode.OK, UserJson)
            .WithRoute("personal_access_tokens/self", HttpStatusCode.OK, "not-valid-json");

        // Act
        Result<TokenValidationOutcome> result = await CreateSut(handler).ValidateTokenAsync(
            ValidBaseUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<TokenValidationOutcome>.Failure>().Error.Code
            .ShouldBe(GitLabErrors.UnexpectedStatusCode(200).Code);
    }
}
