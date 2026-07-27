using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetDefaultBranchAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task WhenGitLabReturnsProject_ReturnsDefaultBranch()
    {
        // Arrange
        string json = """{ "id": 1, "default_branch": "main" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<string> result = await sut.GetDefaultBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<string>.Success success = result.ShouldBeOfType<Result<string>.Success>();
        success.Value.ShouldBe("main");
    }

    [Fact]
    public async Task WhenGitLabReturnsNonSuccessStatus_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<string> result = await sut.GetDefaultBranchAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<string>.Failure failure = result.ShouldBeOfType<Result<string>.Failure>();
        failure.Error.Message.ShouldContain("404");
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
        Result<string> result = await sut.GetDefaultBranchAsync(
            invalidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<string>.Failure failure = result.ShouldBeOfType<Result<string>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        string json = """{ "id": 1, "default_branch": "main" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.GetDefaultBranchAsync(ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject");
    }

    [Fact]
    public async Task WhenCalled_EncodesNestedGroupPath()
    {
        // Arrange
        RepositorySlug nestedSlug = RepositorySlug.Create("group/subgroup/project").ValueOrThrow();
        string json = """{ "id": 1, "default_branch": "develop" }""";
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.GetDefaultBranchAsync(ValidBaseUrl, nestedSlug, "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldContain("group%2Fsubgroup%2Fproject");
    }
}
