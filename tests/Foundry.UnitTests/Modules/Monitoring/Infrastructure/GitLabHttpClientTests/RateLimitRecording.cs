using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class RateLimitRecording
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(
        FakeHandler handler,
        InMemoryProviderRateBudget store,
        FixedTimeProvider? clock = null)
    {
        HttpClient httpClient = new(handler);
        return new GitLabHttpClient(
            httpClient,
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            store,
            (TimeProvider)(clock ?? new FixedTimeProvider(DateTimeOffset.UtcNow)));
    }

    // --- GitLab: RateLimit-Remaining records under GitLabRest ---

    [Fact]
    public async Task WhenResponseHasRateLimitHeaders_RecordsReadingUnderGitLabRestKey()
    {
        // Arrange
        string responseJson = """
            [
              {
                "iid": 1,
                "title": "Test issue",
                "description": "body",
                "author": { "username": "alice" },
                "web_url": "https://gitlab.com/group/project/-/issues/1",
                "labels": ["foundry"]
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        handler.ResponseHeaders["RateLimit-Remaining"] = "1900";
        handler.ResponseHeaders["RateLimit-Limit"] = "2000";
        handler.ResponseHeaders["RateLimit-Reset"] = "1756737600";
        InMemoryProviderRateBudget store = new();
        DateTimeOffset frozenNow = new(2026, 8, 31, 10, 0, 0, TimeSpan.Zero);
        FixedTimeProvider clock = new(frozenNow);
        GitLabHttpClient sut = BuildSut(handler, store, clock);

        // Act
        Result<IssueListing> _ = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        RateBudgetReading? reading = store.TryGet(ProviderBudgetKey.GitLabRest);
        reading.ShouldNotBeNull();
        reading.ShouldSatisfyAllConditions(
            () => reading.Remaining.ShouldBe(1900),
            () => reading.Limit.ShouldBe(2000),
            () => reading.ResetAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(1756737600)),
            () => reading.ObservedAt.ShouldBe(frozenNow));
    }

    [Fact]
    public async Task WhenResponseHasNoRateLimitHeaders_RecordsNothing()
    {
        // Arrange
        string responseJson = """
            [
              {
                "iid": 1,
                "title": "Test issue",
                "description": "body",
                "author": { "username": "alice" },
                "web_url": "https://gitlab.com/group/project/-/issues/1",
                "labels": ["foundry"]
              }
            ]
            """;
        FakeHandler handler = new(HttpStatusCode.OK, responseJson);
        InMemoryProviderRateBudget store = new();
        GitLabHttpClient sut = BuildSut(handler, store);

        // Act
        Result<IssueListing> _ = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        store.TryGet(ProviderBudgetKey.GitLabRest).ShouldBeNull();
    }

    // --- Existing behaviour: 429 still returns RateLimitExhausted ---

    [Fact]
    public async Task WhenResponseIs429_ReturnsRateLimitExhaustedUnchanged()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.TooManyRequests, string.Empty);
        InMemoryProviderRateBudget store = new();
        GitLabHttpClient sut = BuildSut(handler, store);

        // Act
        Result<IssueListing> result = await sut.GetIssuesAsync(
            ValidBaseUrl,
            ValidSlug,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        result.ShouldBeOfType<Result<IssueListing>.Failure>()
            .Error.Code.ShouldBe("GitLab.RateLimitExhausted");
    }

    /// <summary>
    /// A <see cref="TimeProvider"/> that always returns the same frozen timestamp.
    /// </summary>
    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
