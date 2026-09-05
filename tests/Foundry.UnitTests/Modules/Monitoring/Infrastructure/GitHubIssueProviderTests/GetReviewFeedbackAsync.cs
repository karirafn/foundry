using System.Net;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class GetReviewFeedbackAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";
    private const string ValidPrUrl = "https://github.com/owner/repo/pull/1";
    private static readonly DateTimeOffset Since = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    // Fixed point well past all fixture comment timestamps — ensures quiet-period check is deterministic
    private static readonly DateTimeOffset FixedNow = new(2026, 9, 1, 0, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(FakeHandler handler, TimeProvider? timeProvider = null)
    {
        TimeProvider tp = timeProvider ?? new FakeTimeProvider(FixedNow);
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            tp);
        return new GitHubIssueProvider(gitHubHttpClient, new ActionableFeedbackPolicy(tp), ValidToken, ValidBaseUrl);
    }

    private static string BuildGraphQlJson(string commentBody, string createdAt)
    {
        return $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "comments": {
                      "nodes": [
                        {
                          "databaseId": 100,
                          "body": "{{commentBody}}",
                          "createdAt": "{{createdAt}}",
                          "author": { "login": "alice", "__typename": "User" }
                        }
                      ]
                    },
                    "reviewThreads": { "nodes": [] },
                    "reviews": { "nodes": [] }
                  }
                }
              }
            }
            """;
    }

    [Fact]
    public async Task WhenNoteIsAfterSince_ReturnsComment()
    {
        // Arrange
        string json = BuildGraphQlJson("Review comment after cutoff", "2026-06-01T00:00:00Z");
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetReviewFeedbackAsync(
            ValidSlug,
            ValidPrUrl,
            Since,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.Count.ShouldBe(1);
        success.Value.Comments[0].Body.ShouldBe("Review comment after cutoff");
    }

    [Fact]
    public async Task WhenNoteIsBeforeSince_ReturnsNoComments()
    {
        // Arrange
        string json = BuildGraphQlJson("Old comment", "2025-12-31T23:59:59Z");
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetReviewFeedbackAsync(
            ValidSlug,
            ValidPrUrl,
            Since,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenFeedbackHasComments_NewestCommentAtAndOmittedCountArePopulated()
    {
        // Arrange — two comments after since, both pass policy
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "comments": {
                      "nodes": [
                        {
                          "databaseId": 101,
                          "body": "First comment",
                          "createdAt": "2026-06-01T10:00:00Z",
                          "author": { "login": "alice", "__typename": "User" }
                        },
                        {
                          "databaseId": 102,
                          "body": "Second comment",
                          "createdAt": "2026-06-01T11:00:00Z",
                          "author": { "login": "bob", "__typename": "User" }
                        }
                      ]
                    },
                    "reviewThreads": { "nodes": [] },
                    "reviews": { "nodes": [] }
                  }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetReviewFeedbackAsync(
            ValidSlug,
            ValidPrUrl,
            Since,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.ShouldSatisfyAllConditions(
            () => success.Value.Comments.Count.ShouldBe(2),
            () => success.Value.OmittedCommentCount.ShouldBe(0),
            () => success.Value.NewestCommentAt.ShouldBe(new DateTimeOffset(2026, 6, 1, 11, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public async Task WhenNotePassesSinceButIsWithinQuietPeriod_CommentIsHeld()
    {
        // Arrange — comment posted 30 seconds ago is within the 2-minute quiet period and must be held.
        // FakeTimeProvider is pinned so the check is deterministic regardless of wall-clock time.
        DateTimeOffset commentCreatedAt = FixedNow - TimeSpan.FromSeconds(30);
        string createdAtJson = commentCreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "comments": {
                      "nodes": [
                        {
                          "databaseId": 100,
                          "body": "Recent comment within quiet period",
                          "createdAt": "{{createdAtJson}}",
                          "author": { "login": "alice", "__typename": "User" }
                        }
                      ]
                    },
                    "reviewThreads": { "nodes": [] },
                    "reviews": { "nodes": [] }
                  }
                }
              }
            }
            """;
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubIssueProvider sut = BuildSut(handler, new FakeTimeProvider(FixedNow));

        // Act
        Result<ReviewFeedback> result = await sut.GetReviewFeedbackAsync(
            ValidSlug,
            ValidPrUrl,
            Since,
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.ShouldBeEmpty();
    }
}
