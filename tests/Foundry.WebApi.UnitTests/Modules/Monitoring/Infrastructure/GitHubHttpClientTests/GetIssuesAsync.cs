using System.Net;

using Foundry.WebApi.Modules.Monitoring.Domain;
using Foundry.WebApi.Modules.Monitoring.Features;
using Foundry.WebApi.Modules.Monitoring.Infrastructure;
using Foundry.WebApi.Shared.Abstractions;
using Foundry.WebApi.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetIssuesAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("owner/repo")).Value;

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
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<ProviderIssue>> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token123",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<IReadOnlyList<ProviderIssue>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderIssue>>.Success>();
        IReadOnlyList<ProviderIssue> issues = success.Value;
        issues.Count.ShouldBe(1);
        ProviderIssue issue = issues[0];
        issue.ShouldSatisfyAllConditions(
            () => issue.Number.ShouldBe(42),
            () => issue.Title.ShouldBe("Fix the bug"),
            () => issue.Body.ShouldBe("Bug description"),
            () => issue.Author.ShouldBe("octocat"),
            () => issue.Url.ShouldBe("https://github.com/owner/repo/issues/42"),
            () => issue.Labels.ShouldBe(["bug", "foundry"]));
    }

    [Fact]
    public async Task WhenGitHubReturnsIssues_SetsCorrectRequestHeaders()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        await sut.GetIssuesAsync(ValidBaseUrl, ValidSlug, "ghp_mytoken", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.Headers.Authorization.ShouldNotBeNull();
        request.Headers.Authorization.Scheme.ShouldBe("Bearer");
        request.Headers.Authorization.Parameter.ShouldBe("ghp_mytoken");
        request.Headers.Accept.ShouldContain(h => h.MediaType == "application/vnd.github+json");
        request.Headers.Contains("X-GitHub-Api-Version").ShouldBeTrue();
        request.Headers.GetValues("X-GitHub-Api-Version").ShouldContain("2022-11-28");
        request.Headers.UserAgent.ShouldContain(h => h.Product != null && h.Product.Name == "Foundry");
    }

    [Fact]
    public async Task WhenGitHubReturnsIssues_UsesAbsoluteUrlFromBaseUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);
        Uri baseUrl = new("https://api.github.com");

        // Act
        await sut.GetIssuesAsync(baseUrl, ValidSlug, "token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.IsAbsoluteUri.ShouldBeTrue();
        request.RequestUri.Host.ShouldBe("api.github.com");
        request.RequestUri.AbsolutePath.ShouldContain("repos/owner/repo/issues");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<IReadOnlyList<ProviderIssue>> result = await sut.GetIssuesAsync(
            invalidBaseUrl,
            ValidSlug,
            "token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<ProviderIssue>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderIssue>>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenGitHubReturnsNonSuccessStatus_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.InternalServerError, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<ProviderIssue>> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<ProviderIssue>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderIssue>>.Failure>();
        failure.Error.Message.ShouldContain("500");
    }

    [Fact]
    public async Task WhenGitHubReturns403WithRateLimitExhausted_ReturnsRateLimitError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<ProviderIssue>> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<ProviderIssue>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderIssue>>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenGitHubReturns403WithoutRateLimitHeader_ReturnsGenericForbiddenError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

        // Act
        Result<IReadOnlyList<ProviderIssue>> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<ProviderIssue>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderIssue>>.Failure>();
        failure.Error.Code.ShouldNotBe("GitHub.RateLimitExhausted");
    }
}
