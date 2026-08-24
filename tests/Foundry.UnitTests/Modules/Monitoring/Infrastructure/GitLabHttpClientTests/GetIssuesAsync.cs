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
using Foundry.Modules.Monitoring.Features.Providers;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetIssuesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    [Fact]
    public async Task WhenGitLabReturnsIssues_ParsesResponseCorrectly()
    {
        // Arrange
        string json = """
            [
              {
                "iid": 42,
                "title": "Fix the bug",
                "description": "Bug description",
                "author": { "username": "alice" },
                "web_url": "https://gitlab.com/group/project/-/issues/42",
                "labels": [ "bug", "foundry" ]
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token123",
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
            () => issue.Author.ShouldBe("alice"),
            () => issue.Url.ShouldBe("https://gitlab.com/group/project/-/issues/42"),
            () => issue.Labels.ShouldBe(["bug", "foundry"]),
            () => issue.IssueKindLabel.ShouldBe("bug"));
    }

    [Fact]
    public async Task WhenCalled_UsesPrivateTokenHeader()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> _ = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "glpat_mytoken", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Headers.TryGetValues("PRIVATE-TOKEN", out IEnumerable<string>? values).ShouldBeTrue();
        values.ShouldNotBeNull();
        values.FirstOrDefault().ShouldBe("glpat_mytoken");
    }

    [Fact]
    public async Task WhenCalled_UsesUrlEncodedProjectPath()
    {
        // Arrange
        RepositorySlug nestedSlug = RepositorySlug.Create("group/subgroup/project").ValueOrThrow();
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> _ = await sut.GetIssuesAsync(ValidBaseUrl, nestedSlug, "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldContain("group%2Fsubgroup%2Fproject");
    }

    [Fact]
    public async Task WhenCalled_UsesCorrectEndpointUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> _ = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.IsAbsoluteUri.ShouldBeTrue();
        request.RequestUri.AbsolutePath.ShouldContain("projects");
        request.RequestUri.Query.ShouldContain("labels=foundry");
        request.RequestUri.Query.ShouldContain("state=opened");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(
            invalidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IssueListing>.Failure failure =
            result.ShouldBeOfType<Result<IssueListing>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenGitLabReturnsNonSuccessStatus_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IssueListing>.Failure failure =
            result.ShouldBeOfType<Result<IssueListing>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenCalled_RequestUrlIncludesExplicitPerPageAndPageParameters()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> _ = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);

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
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);

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
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);

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
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);

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
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);

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
        GitLabHttpClient sut = new(httpClient, NullLogger<GitLabHttpClient>.Instance, new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "glpat_token", CancellationToken.None);

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
                      "iid": {{i + 1}},
                      "title": "Issue {{i + 1}}",
                      "description": "Body {{i + 1}}",
                      "author": { "username": "user{{i}}" },
                      "web_url": "https://gitlab.com/group/project/-/issues/{{i + 1}}",
                      "labels": [ "foundry" ]
                    }
                    """));
        return $"[{items}]";
    }
}
