using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class IsIssueClosedAsync
{
    // rateLimit block from the 2026-11-27 `gh api graphql` capture on karirafn/foundry.
    private const string CapturedRateLimit = """{"cost":1,"remaining":4990,"limit":5000,"resetAt":"2026-11-27T10:23:06Z"}""";

    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(FakeHandler handler, InMemoryProviderRateBudget? budget = null) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), budget ?? new InMemoryProviderRateBudget(), TimeProvider.System);

    [Fact]
    public async Task WhenGitHubReturnsClosedState_ReturnsTrue()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "issue": { "state": "CLOSED" }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.IsIssueClosedAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenGitHubReturnsOpenState_ReturnsFalse()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "issue": { "state": "OPEN" }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.IsIssueClosedAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<bool>.Success success = result.ShouldBeOfType<Result<bool>.Success>();
        success.Value.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenGitHubReturns403WithRateLimitExhausted_ReturnsRateLimitError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, string.Empty);
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<bool> result = await sut.IsIssueClosedAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<bool>.Failure failure = result.ShouldBeOfType<Result<bool>.Failure>();
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
        Result<bool> result = await sut.IsIssueClosedAsync(
            ValidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<bool>.Failure failure = result.ShouldBeOfType<Result<bool>.Failure>();
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
        Result<bool> result = await sut.IsIssueClosedAsync(
            invalidBaseUrl,
            ValidSlug,
            issueNumber: 42,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<bool>.Failure failure = result.ShouldBeOfType<Result<bool>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenCalled_PostsToGraphQlEndpoint()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "issue": { "state": "OPEN" }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.IsIssueClosedAsync(ValidBaseUrl, ValidSlug, issueNumber: 42, token: "ghp_token", CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/graphql");
        request.Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task WhenCalled_RequestBodyContainsIssueNumber()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "issue": { "state": "OPEN" }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.IsIssueClosedAsync(ValidBaseUrl, ValidSlug, issueNumber: 42, token: "ghp_token", CancellationToken.None);

        // Assert
        string body = handler.LastRequestBody.ShouldNotBeNull();
        body.ShouldContain("42");
        body.ShouldContain("rateLimit");
    }

    [Fact]
    public async Task WhenReading_RecordsTheGraphQlHeadroomFromTheData()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit":{{CapturedRateLimit}},
                "repository": {
                  "issue": { "state": "OPEN" }
                }
              }
            }
            """;
        InMemoryProviderRateBudget budget = new();
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, json), budget);

        // Act
        await sut.IsIssueClosedAsync(ValidBaseUrl, ValidSlug, issueNumber: 42, token: "ghp_token", CancellationToken.None);

        // Assert
        RateBudgetReading reading = budget.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldNotBeNull();
        reading.ShouldSatisfyAllConditions(
            () => reading.Remaining.ShouldBe(4990),
            () => reading.Limit.ShouldBe(5000),
            () => reading.ResetAt.ShouldBe(new DateTimeOffset(2026, 11, 27, 10, 23, 6, TimeSpan.Zero)));
    }
}
