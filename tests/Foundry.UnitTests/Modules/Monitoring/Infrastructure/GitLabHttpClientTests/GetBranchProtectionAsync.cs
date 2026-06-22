using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetBranchProtectionAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task WhenApiReturns404_ReturnsNoProtection()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<BranchRules> result = await sut.GetBranchProtectionAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchRules>.Success success = result.ShouldBeOfType<Result<BranchRules>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.RejectDirectPushes.ShouldBeFalse(),
            () => success.Value.RejectForcePushes.ShouldBeFalse(),
            () => success.Value.RejectDeletion.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenBranchForcePushIsAllowed_RejectsNothingByDefault()
    {
        // Arrange
        string json = """
            {
              "name": "main",
              "allow_force_push": true,
              "push_access_levels": [{ "access_level": 40 }]
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<BranchRules> result = await sut.GetBranchProtectionAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchRules>.Success success = result.ShouldBeOfType<Result<BranchRules>.Success>();
        success.Value.RejectForcePushes.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenBranchForcePushIsDisabled_RejectsForcePushes()
    {
        // Arrange
        string json = """
            {
              "name": "main",
              "allow_force_push": false,
              "push_access_levels": [{ "access_level": 40 }]
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<BranchRules> result = await sut.GetBranchProtectionAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<BranchRules>.Success success = result.ShouldBeOfType<Result<BranchRules>.Success>();
        success.Value.RejectForcePushes.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenApiReturnsNonSuccessNonNotFound_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<BranchRules> result = await sut.GetBranchProtectionAsync(
            ValidBaseUrl,
            ValidSlug,
            "main",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<BranchRules>.Failure failure = result.ShouldBeOfType<Result<BranchRules>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<BranchRules> result = await sut.GetBranchProtectionAsync(
            invalidBaseUrl,
            ValidSlug,
            "main",
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<BranchRules>.Failure failure = result.ShouldBeOfType<Result<BranchRules>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.GetBranchProtectionAsync(ValidBaseUrl, ValidSlug, "main", "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/protected_branches/main");
    }
}
