using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
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

public sealed class GetIssuesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    [Fact]
    public async Task WhenGitHubReturnsIssues_ParsesResponseCorrectly()
    {
        // Arrange
        string json = """
            [
              {
                "number": 42,
                "title": "Fix the bug",
                "body": "Bug description",
                "user": { "login": "octocat" },
                "html_url": "https://github.com/owner/repo/issues/42",
                "labels": [
                  { "name": "bug" },
                  { "name": "foundry" }
                ]
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IssueListing>.Success success =
            result.ShouldBeOfType<Result<IssueListing>.Success>();
        IReadOnlyList<ProviderIssue> issues = success.Value.Issues;
        issues.Count.ShouldBe(1);
        ProviderIssue issue = issues[0];
        issue.ShouldSatisfyAllConditions(
            () => issue.Number.ShouldBe(42),
            () => issue.Title.ShouldBe("Fix the bug"),
            () => issue.Body.ShouldBe("Bug description"),
            () => issue.Author.ShouldBe("octocat"),
            () => issue.Url.ShouldBe("https://github.com/owner/repo/issues/42"),
            () => issue.Labels.ShouldBe(["bug", "foundry"]),
            () => issue.IssueKindLabel.ShouldBe("bug"));
    }

    [Fact]
    public async Task WhenGitHubReturnsIssues_SetsCorrectRequestHeaders()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> _ = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "ghp_mytoken", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Headers.Authorization.ShouldNotBeNull();
        request.Headers.Authorization.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe("ghp_mytoken");
        request.Headers.Accept.ShouldContain(h => h.MediaType == "application/vnd.github+json");
        request.Headers.Contains("X-GitHub-Api-Version").ShouldBeTrue();
        request.Headers.GetValues("X-GitHub-Api-Version").ShouldContain("2026-03-10");
        request.Headers.UserAgent.ShouldContain(h => h.Product != null && h.Product.Name == "Foundry");
    }

    [Fact]
    public async Task WhenGitHubReturnsIssues_UsesAbsoluteUrlFromBaseUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        Uri baseUrl = new("https://api.github.com");

        // Act
        Result<IssueListing> _ = await sut.GetIssuesAsync(baseUrl, ValidSlug, "token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.IsAbsoluteUri.ShouldBeTrue();
        request.RequestUri.Host.ShouldBe("api.github.com");
        request.RequestUri.AbsolutePath.ShouldContain("repos/owner/repo/issues");
    }

    [Fact]
    public async Task WhenCalled_RequestUrlIncludesExplicitPerPageAndPageParameters()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> _ = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.Query.ShouldContain("per_page=100");
        request.RequestUri.Query.ShouldContain("page=1");
    }

    [Fact]
    public async Task WhenSingleShortPageReturned_IssueListing_IsComplete()
    {
        // Arrange
        string json = BuildIssuePageJson(3);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        Result<IssueListing>.Success success = result.ShouldBeOfType<Result<IssueListing>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsComplete.ShouldBeTrue(),
            () => success.Value.Issues.Count.ShouldBe(3));
    }

    [Fact]
    public async Task WhenOneFullPageThenEmptyPage_ReturnsAllIssuesAndIsComplete()
    {
        // Arrange — exactly one full page of 100, then an empty page
        string fullPage = BuildIssuePageJson(100);
        string emptyPage = "[]";

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, fullPage),
            (HttpStatusCode.OK, emptyPage),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        Result<IssueListing>.Success success = result.ShouldBeOfType<Result<IssueListing>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsComplete.ShouldBeTrue(),
            () => success.Value.Issues.Count.ShouldBe(100));
    }

    [Fact]
    public async Task WhenTwoFullPagesThenShortPage_ReturnsAllAccumulatedIssuesAndIsComplete()
    {
        // Arrange
        string page1 = BuildIssuePageJson(100, startIndex: 0);
        string page2 = BuildIssuePageJson(100, startIndex: 100);
        string page3 = BuildIssuePageJson(42, startIndex: 200);

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, page1),
            (HttpStatusCode.OK, page2),
            (HttpStatusCode.OK, page3),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        Result<IssueListing>.Success success = result.ShouldBeOfType<Result<IssueListing>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsComplete.ShouldBeTrue(),
            () => success.Value.Issues.Count.ShouldBe(242));
    }

    [Fact]
    public async Task WhenPageCapReachedWithoutShortPage_ReturnsAccumulatedIssuesWithIsCompleteFalse()
    {
        // Arrange — 20 full pages, all 100 items each — hits cap without short page
        SequentialFakeHandler handler = new(
            Enumerable.Range(0, 20).Select(i =>
                (HttpStatusCode.OK, BuildIssuePageJson(100, startIndex: i * 100))));
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        Result<IssueListing>.Success success = result.ShouldBeOfType<Result<IssueListing>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsComplete.ShouldBeFalse(),
            () => success.Value.Issues.Count.ShouldBe(2000));
    }

    [Fact]
    public async Task WhenPage2ReturnsFail_ReturnsFailureWithNoPartialValue()
    {
        // Arrange — first page succeeds, second returns 500
        string page1 = BuildIssuePageJson(100, startIndex: 0);

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, page1),
            (HttpStatusCode.InternalServerError, string.Empty),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "ghp_token", CancellationToken.None);

        // Assert
        Result<IssueListing>.Failure failure = result.ShouldBeOfType<Result<IssueListing>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    private static string BuildIssuePageJson(int count, int startIndex = 0)
    {
        string items = string.Join(
            ",",
            Enumerable
                .Range(startIndex, count)
                .Select(i => $$"""
                    {
                      "number": {{i + 1}},
                      "title": "Issue {{i + 1}}",
                      "body": "Body {{i + 1}}",
                      "user": { "login": "user{{i}}" },
                      "html_url": "https://github.com/owner/repo/issues/{{i + 1}}",
                      "labels": [{ "name": "foundry" }]
                    }
                    """));
        return $"[{items}]";
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(
            invalidBaseUrl,
            ValidSlug,
            "token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IssueListing>.Failure failure =
            result.ShouldBeOfType<Result<IssueListing>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenGitHubReturnsNonSuccessStatus_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IssueListing>.Failure failure =
            result.ShouldBeOfType<Result<IssueListing>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenGitHubReturns403WithRateLimitExhausted_ReturnsRateLimitError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IssueListing>.Failure failure =
            result.ShouldBeOfType<Result<IssueListing>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenGitHubReturns403WithoutRateLimitHeader_ReturnsGenericForbiddenError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient, NullLogger<GitHubHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IssueListing>.Failure failure =
            result.ShouldBeOfType<Result<IssueListing>.Failure>();
        failure.Error.Code.ShouldNotBe("GitHub.RateLimitExhausted");
    }
}
