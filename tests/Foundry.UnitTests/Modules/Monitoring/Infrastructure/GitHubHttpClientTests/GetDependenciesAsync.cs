using System.Net;

using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetDependenciesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Fact]
    public async Task WhenGitHubReturns403WithRateLimitExhausted_ReturnsRateLimitError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            invalidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenGitHubReturnsNonSuccessStatus_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenCalled_SetsCorrectRequestHeaders()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        await sut.GetDependenciesAsync(ValidBaseUrl, ValidSlug, issueNumber: 42, token: "ghp_mytoken", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.ShouldSatisfyAllConditions(
            () => request.Headers.Authorization.ShouldNotBeNull(),
            () => request.Headers.Authorization!.Scheme.ShouldBe("Bearer"),
            () => request.Headers.Authorization!.Parameter.ShouldBe("ghp_mytoken"),
            () => request.Headers.Accept.ShouldContain(h => h.MediaType == "application/vnd.github+json"),
            () => request.Headers.GetValues("X-GitHub-Api-Version").ShouldContain("2026-03-10"),
            () => request.Headers.UserAgent.ShouldContain(h => h.Product != null && h.Product.Name == "Foundry"));
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        await sut.GetDependenciesAsync(ValidBaseUrl, ValidSlug, issueNumber: 42, token: "ghp_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/repos/owner/repo/issues/42/dependencies/blocked_by");
    }

    [Fact]
    public async Task WhenGitHubReturnsEmptyArray_ReturnsEmptyList()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenDependenciesIncludeCrossRepoDeps_FiltersToSameRepoOnly()
    {
        // Arrange
        string json = """
            [
              {
                "number": 10,
                "title": "Same-repo dependency",
                "repository": { "full_name": "owner/repo" }
              },
              {
                "number": 99,
                "title": "Cross-repo dependency",
                "repository": { "full_name": "other-owner/other-repo" }
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10]);
    }

    [Fact]
    public async Task WhenGitHubReturnsDependencies_ReturnsSameRepoIssueNumbers()
    {
        // Arrange
        string json = """
            [
              {
                "number": 10,
                "title": "Dependency one",
                "repository": { "full_name": "owner/repo" }
              },
              {
                "number": 20,
                "title": "Dependency two",
                "repository": { "full_name": "owner/repo" }
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10, 20]);
    }

    [Fact]
    public async Task WhenBlockerHasUnrecognizedState_IncludesItInResults()
    {
        // Arrange
        string json = """
            [
              {
                "number": 10,
                "title": "Blocker with unknown state",
                "state": "draft",
                "repository": { "full_name": "owner/repo" }
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10]);
    }

    [Fact]
    public async Task WhenBlockerHasNoStateField_IncludesItInResults()
    {
        // Arrange
        string json = """
            [
              {
                "number": 10,
                "title": "Blocker without state",
                "repository": { "full_name": "owner/repo" }
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10]);
    }

    [Fact]
    public async Task WhenBlockerIsOpen_IncludesItInResults()
    {
        // Arrange
        string json = """
            [
              {
                "number": 10,
                "title": "Open blocker",
                "state": "open",
                "repository": { "full_name": "owner/repo" }
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10]);
    }

    [Fact]
    public async Task WhenBlockerIsClosed_ExcludesItFromResults()
    {
        // Arrange
        string json = """
            [
              {
                "number": 10,
                "title": "Open blocker",
                "state": "open",
                "repository": { "full_name": "owner/repo" }
              },
              {
                "number": 20,
                "title": "Closed blocker",
                "state": "closed",
                "repository": { "full_name": "owner/repo" }
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance);

        // Act
        Result<IReadOnlyList<int>> result = await sut.GetDependenciesAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<int>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<int>>.Success>();
        success.Value.ShouldBe([10]);
    }
}
