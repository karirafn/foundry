using System.Net;

using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class ValidateTokenAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    [Fact]
    public async Task WhenTokenIsValid_ReturnsValid()
    {
        // Arrange
        string json = """{ "id": 1, "username": "alice" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
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
        string json = """{ "id": 1, "username": "alice" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.ValidateTokenAsync(ValidBaseUrl, "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/api/v4/user");
    }

    [Fact]
    public async Task WhenCalled_UsesPrivateTokenHeader()
    {
        // Arrange
        string json = """{ "id": 1, "username": "alice" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
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
    public async Task WhenResponseBodyContainsUsername_AccountNameIsResolved()
    {
        // Arrange
        string json = """{ "id": 1, "username": "alice" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
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
        string json = """{ "id": 1, "username": "" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
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
        string json = """{ "id": 1 }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
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
