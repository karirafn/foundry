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

public sealed class RateLimitRecording
{
    private static readonly Uri GitHubComBaseUrl = new("https://api.github.com");

    private static GitHubHttpClient BuildSut(
        FakeHandler handler,
        InMemoryProviderRateBudget store,
        FixedTimeProvider? clock = null)
    {
        HttpClient httpClient = new(handler);
        return new GitHubHttpClient(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            store,
            (TimeProvider)(clock ?? new FixedTimeProvider(DateTimeOffset.UtcNow)));
    }

    // --- GraphQL: rateLimit block records GitHubGraphQl reading ---

    [Fact]
    public async Task WhenGraphQlDataHasRateLimitBlock_RecordsReadingUnderGitHubGraphQlKey()
    {
        // Arrange
        string responseJson = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4200, "limit": 5000, "resetAt": "2026-09-01T12:00:00Z" },
                "state": "OPEN"
              },
              "errors": null
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        InMemoryProviderRateBudget store = new();
        DateTimeOffset frozenNow = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
        FixedTimeProvider clock = new(frozenNow);
        GitHubHttpClient sut = BuildSut(handler, store, clock);

        // Act
        Result<TestData> _ = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        RateBudgetReading? reading = store.TryGet(ProviderBudgetKey.GitHubGraphQl);
        reading.ShouldNotBeNull();
        reading.ShouldSatisfyAllConditions(
            () => reading.Remaining.ShouldBe(4200),
            () => reading.Limit.ShouldBe(5000),
            () => reading.ResetAt.ShouldBe(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
            () => reading.ObservedAt.ShouldBe(frozenNow));
    }

    [Fact]
    public async Task WhenGraphQlResponseHasNoRateLimitBlock_RecordsNothing()
    {
        // Arrange
        string responseJson = """{"data": { "state": "OPEN" }, "errors": null}""";
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        InMemoryProviderRateBudget store = new();
        GitHubHttpClient sut = BuildSut(handler, store);

        // Act
        Result<TestData> _ = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        store.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldBeNull();
    }

    [Fact]
    public async Task WhenAMutationShapedResponseCarriesNoRateLimitBlock_RecordsNothingAndReturnsTheData()
    {
        // Arrange
        string responseJson = """{"data": { "markPullRequestReadyForReview": { "pullRequest": { "isDraft": false } } }}""";
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        InMemoryProviderRateBudget store = new();
        GitHubHttpClient sut = BuildSut(handler, store);

        // Act
        Result<MutationData> result = await sut.ExecuteGraphQlAsync<MutationData>(
            GitHubComBaseUrl,
            "mutation { markPullRequestReadyForReview(input: {}) { pullRequest { isDraft } } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        store.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldBeNull();
        result.ShouldBeOfType<Result<MutationData>.Success>()
            .Value.MarkPullRequestReadyForReview.ShouldNotBeNull()
            .PullRequest.ShouldNotBeNull()
            .IsDraft.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenDataIsNullAndErrorsArePresent_RecordsNothingAndFailsWithTheGraphQlError()
    {
        // Arrange
        string responseJson = """{"data": null, "errors": [ { "type": "FORBIDDEN", "message": "Resource not accessible by integration" } ]}""";
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        InMemoryProviderRateBudget store = new();
        GitHubHttpClient sut = BuildSut(handler, store);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        store.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldBeNull();
        result.ShouldBeOfType<Result<TestData>.Failure>().Error.Code.ShouldBe(GitHubErrors.GraphQlErrorCode);
    }

    [Fact]
    public async Task WhenErrorsAccompanyADataRateLimitBlock_RecordsTheReadingAndStillFails()
    {
        // Arrange
        string responseJson = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4200, "limit": 5000, "resetAt": "2026-09-01T12:00:00Z" },
                "state": null
              },
              "errors": [ { "type": "NOT_FOUND", "message": "Could not resolve to an issue." } ]
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        InMemoryProviderRateBudget store = new();
        GitHubHttpClient sut = BuildSut(handler, store);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        store.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldNotBeNull().Remaining.ShouldBe(4200);
        Error error = result.ShouldBeOfType<Result<TestData>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(GitHubErrors.GraphQlErrorCode),
            () => error.Kind.ShouldBe(ErrorKind.NotFound));
    }

    [Fact]
    public async Task WhenARateLimitedErrorAccompaniesADataRateLimitBlock_RecordsTheReadingAndFailsAsExhausted()
    {
        // Arrange
        string responseJson = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 0, "limit": 5000, "resetAt": "2026-09-01T12:00:00Z" },
                "state": null
              },
              "errors": [ { "type": "RATE_LIMITED", "message": "API rate limit exceeded" } ]
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        InMemoryProviderRateBudget store = new();
        GitHubHttpClient sut = BuildSut(handler, store);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        store.TryGet(ProviderBudgetKey.GitHubGraphQl).ShouldNotBeNull().Remaining.ShouldBe(0);
        result.ShouldBeOfType<Result<TestData>.Failure>().Error.Code.ShouldBe(GitHubErrors.RateLimitExhausted.Code);
    }

    // --- REST: X-RateLimit headers record GitHubRest reading ---

    [Fact]
    public async Task WhenRestResponseHasXRateLimitHeaders_RecordsReadingUnderGitHubRestKey()
    {
        // Arrange
        // GetBranchRulesAsync returns 404 -> BranchRules(false, false, false) which still records headroom
        FakeHandler handler = new(HttpStatusCode.NotFound, "{}");
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "3800";
        handler.ResponseHeaders["X-RateLimit-Limit"] = "5000";
        handler.ResponseHeaders["X-RateLimit-Reset"] = "1756737600";
        InMemoryProviderRateBudget store = new();
        DateTimeOffset frozenNow = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
        FixedTimeProvider clock = new(frozenNow);
        GitHubHttpClient sut = BuildSut(handler, store, clock);

        // Act
        Result<BranchRules> _ = await sut.GetBranchRulesAsync(
            GitHubComBaseUrl,
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "main",
            "ghp_token",
            CancellationToken.None);

        // Assert
        RateBudgetReading? reading = store.TryGet(ProviderBudgetKey.GitHubRest);
        reading.ShouldNotBeNull();
        reading.ShouldSatisfyAllConditions(
            () => reading.Remaining.ShouldBe(3800),
            () => reading.Limit.ShouldBe(5000),
            () => reading.ResetAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1756737600)),
            () => reading.ObservedAt.ShouldBe(frozenNow));
    }

    [Fact]
    public async Task WhenRestResponseHasNoXRateLimitHeaders_RecordsNothing()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.NotFound, "{}");
        InMemoryProviderRateBudget store = new();
        GitHubHttpClient sut = BuildSut(handler, store);

        // Act
        Result<BranchRules> _ = await sut.GetBranchRulesAsync(
            GitHubComBaseUrl,
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "main",
            "ghp_token",
            CancellationToken.None);

        // Assert
        store.TryGet(ProviderBudgetKey.GitHubRest).ShouldBeNull();
    }

    // --- Existing behaviour: GraphQL result unchanged when rateLimit block is present ---

    [Fact]
    public async Task WhenGraphQlResponseWithRateLimitSucceeds_ReturnsOkWithDataUnchanged()
    {
        // Arrange
        string responseJson = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4200, "limit": 5000, "resetAt": "2026-09-01T12:00:00Z" },
                "state": "OPEN"
              },
              "errors": null
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        InMemoryProviderRateBudget store = new();
        GitHubHttpClient sut = BuildSut(handler, store);

        // Act
        Result<TestData> result = await sut.ExecuteGraphQlAsync<TestData>(
            GitHubComBaseUrl,
            "query { viewer { login } }",
            new { },
            "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<TestData>.Success success = result.ShouldBeOfType<Result<TestData>.Success>();
        success.Value.State.ShouldBe("OPEN");
    }

    // --- REST response header absent/unparseable for Reset does not record ---

    [Fact]
    public async Task WhenXRateLimitRemainingHeaderIsAbsent_RecordsNothing()
    {
        // Arrange — only Limit and Reset present, Remaining absent
        FakeHandler handler = new(HttpStatusCode.NotFound, "{}");
        handler.ResponseHeaders["X-RateLimit-Limit"] = "5000";
        handler.ResponseHeaders["X-RateLimit-Reset"] = "1756737600";
        InMemoryProviderRateBudget store = new();
        GitHubHttpClient sut = BuildSut(handler, store);

        // Act
        Result<BranchRules> _ = await sut.GetBranchRulesAsync(
            GitHubComBaseUrl,
            RepositorySlug.Create("owner/repo").ValueOrThrow(),
            "main",
            "ghp_token",
            CancellationToken.None);

        // Assert
        store.TryGet(ProviderBudgetKey.GitHubRest).ShouldBeNull();
    }

    internal sealed record TestData(string? State);

    internal sealed record MutationData(MarkReadyPayload? MarkPullRequestReadyForReview);

    internal sealed record MarkReadyPayload(MarkReadyPullRequest? PullRequest);

    internal sealed record MarkReadyPullRequest(bool IsDraft);

    /// <summary>
    /// A <see cref="TimeProvider"/> that always returns the same frozen timestamp.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
