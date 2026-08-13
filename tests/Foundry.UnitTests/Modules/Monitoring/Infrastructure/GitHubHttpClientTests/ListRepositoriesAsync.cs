using System.Net;

using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class ListRepositoriesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static string BuildPageJson(int count, int startIndex = 0)
    {
        string items = string.Join(
            ",",
            Enumerable
                .Range(startIndex, count)
                .Select(i => $$$"""{"full_name":"owner/repo-{{{i}}}","private":false,"permissions":{"push":true}}"""));
        return $"[{items}]";
    }

    [Fact]
    public async Task WhenGitHubReturnsSinglePage_ParsesRepositoriesCorrectly()
    {
        // Arrange
        string json = """
            [
              {
                "full_name": "octocat/Hello-World",
                "private": false,
                "permissions": { "push": true }
              },
              {
                "full_name": "octocat/my-secret",
                "private": true,
                "permissions": { "push": false }
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "ghp_token123",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderRepository>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>();
        IReadOnlyList<ProviderRepository> repos = success.Value;
        repos.Count.ShouldBe(2);
        repos.ShouldContain(r => r.Slug == "octocat/Hello-World" && !r.IsPrivate && r.CanPush);
        repos.ShouldContain(r => r.Slug == "octocat/my-secret" && r.IsPrivate && !r.CanPush);
    }

    [Fact]
    public async Task WhenPermissionsFieldIsAbsent_CanPushDefaultsFalse()
    {
        // Arrange — classic PAT response has no per-repo permissions object
        string json = """
            [
              { "full_name": "org/repo-a", "private": false }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "ghp_token123",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderRepository>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>();
        success.Value.ShouldContain(r => r.Slug == "org/repo-a" && !r.CanPush);
    }

    [Fact]
    public async Task WhenPermissionsPushIsTrue_CanPushIsTrue()
    {
        // Arrange
        string json = """
            [
              {
                "full_name": "octocat/writable-repo",
                "private": false,
                "permissions": { "push": true }
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "ghp_token123",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderRepository>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>();
        success.Value.ShouldContain(r => r.Slug == "octocat/writable-repo" && r.CanPush);
    }

    [Fact]
    public async Task WhenPermissionsPushIsFalse_CanPushIsFalse()
    {
        // Arrange
        string json = """
            [
              {
                "full_name": "octocat/read-only-repo",
                "private": true,
                "permissions": { "push": false }
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "ghp_token123",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderRepository>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>();
        success.Value.ShouldContain(r => r.Slug == "octocat/read-only-repo" && !r.CanPush);
    }

    [Fact]
    public async Task WhenGitHubReturnsMultipleFullPages_PaginatesAndCombinesAllResults()
    {
        // Arrange
        // Page 1: 100 items, page 2: 50 items (< 100, stop)
        string page1Json = BuildPageJson(100, startIndex: 0);
        string page2Json = BuildPageJson(50, startIndex: 100);

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, page1Json),
            (HttpStatusCode.OK, page2Json),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "ghp_token123",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderRepository>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>();
        success.Value.Count.ShouldBe(150);
    }

    [Fact]
    public async Task WhenCalled_SetsCorrectRequestHeaders()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        await sut.ListRepositoriesAsync(ValidBaseUrl, "ghp_mytoken", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Headers.Authorization.ShouldNotBeNull();
        request.Headers.Authorization.ShouldSatisfyAllConditions(
            () => request.Headers.Authorization.Scheme.ShouldBe("Bearer"),
            () => request.Headers.Authorization.Parameter.ShouldBe("ghp_mytoken"));
        request.Headers.Accept.ShouldContain(h => h.MediaType == "application/vnd.github+json");
        request.Headers.Contains("X-GitHub-Api-Version").ShouldBeTrue();
        request.Headers.GetValues("X-GitHub-Api-Version").ShouldContain("2026-03-10");
        request.Headers.UserAgent.ShouldContain(h => h.Product != null && h.Product.Name == "Foundry");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        await sut.ListRepositoriesAsync(ValidBaseUrl, "ghp_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.IsAbsoluteUri.ShouldBeTrue();
        request.RequestUri.Host.ShouldBe("api.github.com");
        request.RequestUri.AbsolutePath.ShouldBe("/user/repos");
        request.RequestUri.Query.ShouldContain("sort=full_name");
        request.RequestUri.Query.ShouldContain("per_page=100");
        request.RequestUri.Query.ShouldContain("page=1");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.ListRepositoriesAsync(
            invalidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderRepository>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenGitHubReturns403WithRateLimit_ReturnsRateLimitError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderRepository>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenGitHubReturns403WithoutRateLimit_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Failure>();
    }

    [Fact]
    public async Task WhenGitHubReturnsNonSuccessStatus_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "ghp_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderRepository>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenAllPagesAreFull_StopsAfterFivePages()
    {
        // Arrange
        // 5 full pages of 100 items each — must stop after page 5
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, BuildPageJson(100, startIndex: 0)),
            (HttpStatusCode.OK, BuildPageJson(100, startIndex: 100)),
            (HttpStatusCode.OK, BuildPageJson(100, startIndex: 200)),
            (HttpStatusCode.OK, BuildPageJson(100, startIndex: 300)),
            (HttpStatusCode.OK, BuildPageJson(100, startIndex: 400)),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IReadOnlyList<ProviderRepository>> result = await sut.ListRepositoriesAsync(
            ValidBaseUrl,
            "ghp_token123",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderRepository>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderRepository>>.Success>();
        success.Value.Count.ShouldBe(500);
    }
}
