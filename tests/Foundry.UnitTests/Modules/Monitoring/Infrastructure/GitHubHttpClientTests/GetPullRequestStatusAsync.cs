using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class GetPullRequestStatusAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))));

    [Fact]
    public async Task WhenPrIsMerged_ReturnsMergedAndClosedStatus()
    {
        // Arrange
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": { "state": "CLOSED", "merged": true }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<PullRequestStatus>.Success success = result.ShouldBeOfType<Result<PullRequestStatus>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsMerged.ShouldBeTrue(),
            () => success.Value.IsClosed.ShouldBeTrue());
    }

    [Fact]
    public async Task WhenPrIsClosedWithoutMerge_ReturnsClosedNotMerged()
    {
        // Arrange
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": { "state": "CLOSED", "merged": false }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<PullRequestStatus>.Success success = result.ShouldBeOfType<Result<PullRequestStatus>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsClosed.ShouldBeTrue(),
            () => success.Value.IsMerged.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenPrIsOpen_ReturnsNotClosedNotMerged()
    {
        // Arrange
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": { "state": "OPEN", "merged": false }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<PullRequestStatus>.Success success = result.ShouldBeOfType<Result<PullRequestStatus>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.IsClosed.ShouldBeFalse(),
            () => success.Value.IsMerged.ShouldBeFalse());
    }

    [Fact]
    public async Task WhenPrUrlCannotBeParsed_ReturnsInvalidUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/issues/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<PullRequestStatus>.Failure failure = result.ShouldBeOfType<Result<PullRequestStatus>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidPullRequestUrl");
    }

    [Fact]
    public async Task WhenGitHubReturns403WithRateLimitExhausted_ReturnsRateLimitError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<PullRequestStatus>.Failure failure = result.ShouldBeOfType<Result<PullRequestStatus>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenGraphQlReturnsRateLimitedError_ReturnsRateLimitError()
    {
        // Arrange
        string json = """
            {
              "data": null,
              "errors": [{ "message": "API rate limit exceeded", "type": "RATE_LIMITED" }]
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<PullRequestStatus>.Failure failure = result.ShouldBeOfType<Result<PullRequestStatus>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenBaseUrlHasInvalidScheme_ReturnsInvalidBaseUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        GitHubHttpClient sut = BuildSut(handler);
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<PullRequestStatus> result = await sut.GetPullRequestStatusAsync(
            invalidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<PullRequestStatus>.Failure failure = result.ShouldBeOfType<Result<PullRequestStatus>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCalled_PostsToGraphQlEndpoint()
    {
        // Arrange
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": { "state": "OPEN", "merged": false }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/graphql");
        request.Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task WhenCalled_RequestBodyContainsPrNumber()
    {
        // Arrange
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": { "state": "OPEN", "merged": false }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetPullRequestStatusAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/pull/123",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        string body = handler.LastRequestBody.ShouldNotBeNull();
        body.ShouldContain("123");
        body.ShouldContain("rateLimit");
    }
}
