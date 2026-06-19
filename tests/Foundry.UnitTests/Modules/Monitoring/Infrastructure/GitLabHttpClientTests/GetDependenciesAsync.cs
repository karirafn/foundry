using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetDependenciesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("group/project")).Value;

    [Fact]
    public async Task WhenGitLabReturnsLinks_ReturnsBlockedByIssueNumbers()
    {
        // Arrange
        string json = """
            [
              {
                "iid": 10,
                "title": "Dependency one",
                "link_type": "is_blocked_by"
              },
              {
                "iid": 20,
                "title": "Dependency two",
                "link_type": "is_blocked_by"
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10, 20]);
    }

    [Fact]
    public async Task WhenLinksContainNonBlockedByTypes_FiltersToBlockedByOnly()
    {
        // Arrange
        string json = """
            [
              {
                "iid": 10,
                "title": "Blocked by",
                "link_type": "is_blocked_by"
              },
              {
                "iid": 99,
                "title": "Related",
                "link_type": "relates_to"
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10]);
    }

    [Fact]
    public async Task WhenGitLabReturns403_ReturnsEmptyList()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGitLabReturns404_ReturnsEmptyList()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenGitLabReturnsNonSuccessStatus_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            invalidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/api/v4/projects/group%2Fproject/issues/42/links");
    }
}
