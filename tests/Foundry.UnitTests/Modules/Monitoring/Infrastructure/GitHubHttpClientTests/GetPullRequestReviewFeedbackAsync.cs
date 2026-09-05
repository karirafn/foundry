using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
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

public sealed class GetPullRequestReviewFeedbackAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidPrUrl = "https://github.com/owner/repo/pull/1";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static string BuildThreeSurfaceJson(
        string conversationCommentBody = "conversation comment",
        string conversationCommentCreatedAt = "2026-06-01T10:00:00Z",
        string conversationCommentAuthorLogin = "alice",
        string conversationCommentAuthorTypename = "User",
        long? conversationCommentDatabaseId = 1001,
        bool reviewThreadIsResolved = false,
        string threadCommentBody = "thread comment",
        string threadCommentCreatedAt = "2026-06-01T11:00:00Z",
        string threadCommentAuthorLogin = "bob",
        string threadCommentAuthorTypename = "User",
        long? threadCommentDatabaseId = 2001,
        string? threadCommentPath = "src/Foo.cs",
        int? threadCommentLine = 42,
        int? threadCommentOriginalLine = null,
        string reviewBody = "review summary",
        string reviewSubmittedAt = "2026-06-01T12:00:00Z",
        string reviewAuthorLogin = "carol",
        string reviewAuthorTypename = "User")
    {
        string pathJson = threadCommentPath is not null ? $"\"{threadCommentPath}\"" : "null";
        string lineJson = threadCommentLine.HasValue
            ? threadCommentLine.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "null";
        string origLineJson = threadCommentOriginalLine.HasValue
            ? threadCommentOriginalLine.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "null";
        string convDatabaseIdJson = conversationCommentDatabaseId.HasValue
            ? conversationCommentDatabaseId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "null";
        string threadDatabaseIdJson = threadCommentDatabaseId.HasValue
            ? threadCommentDatabaseId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "null";

        return $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "comments": {
                      "nodes": [
                        {
                          "databaseId": {{convDatabaseIdJson}},
                          "body": "{{conversationCommentBody}}",
                          "createdAt": "{{conversationCommentCreatedAt}}",
                          "author": { "login": "{{conversationCommentAuthorLogin}}", "__typename": "{{conversationCommentAuthorTypename}}" }
                        }
                      ]
                    },
                    "reviewThreads": {
                      "nodes": [
                        {
                          "isResolved": {{(reviewThreadIsResolved ? "true" : "false")}},
                          "comments": {
                            "nodes": [
                              {
                                "databaseId": {{threadDatabaseIdJson}},
                                "body": "{{threadCommentBody}}",
                                "path": {{pathJson}},
                                "line": {{lineJson}},
                                "originalLine": {{origLineJson}},
                                "createdAt": "{{threadCommentCreatedAt}}",
                                "author": { "login": "{{threadCommentAuthorLogin}}", "__typename": "{{threadCommentAuthorTypename}}" }
                              }
                            ]
                          }
                        }
                      ]
                    },
                    "reviews": {
                      "nodes": [
                        {
                          "body": "{{reviewBody}}",
                          "submittedAt": "{{reviewSubmittedAt}}",
                          "author": { "login": "{{reviewAuthorLogin}}", "__typename": "{{reviewAuthorTypename}}" }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;
    }

    [Fact]
    public async Task WhenCalled_PostsToGraphQlEndpointWithSingleRequest()
    {
        // Arrange
        string json = BuildThreeSurfaceJson();
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        handler.AllRequests.Count.ShouldBe(1);
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.AbsolutePath.ShouldBe("/graphql");
        request.Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task WhenCalled_RequestBodyContainsAllThreeSurfaces()
    {
        // Arrange
        string json = BuildThreeSurfaceJson();
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        string body = handler.LastRequestBody.ShouldNotBeNull();
        body.ShouldContain("comments");
        body.ShouldContain("reviewThreads");
        body.ShouldContain("reviews");
        body.ShouldContain("submittedAt");
        body.ShouldContain("__typename");
        body.ShouldNotContain("CHANGES_REQUESTED");
    }

    [Fact]
    public async Task WhenReviewThreadIsResolved_MapsThreadResolvedTrue()
    {
        // Arrange
        string json = BuildThreeSurfaceJson(reviewThreadIsResolved: true);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.ShouldContain(c => c.Origin == CommentOrigin.ReviewThread);
        ProviderComment threadComment = comments.Single(c => c.Origin == CommentOrigin.ReviewThread);
        threadComment.ThreadResolved.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenReviewThreadIsNotResolved_MapsThreadResolvedFalse()
    {
        // Arrange
        string json = BuildThreeSurfaceJson(reviewThreadIsResolved: false);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.ShouldContain(c => c.Origin == CommentOrigin.ReviewThread);
        ProviderComment threadComment = comments.Single(c => c.Origin == CommentOrigin.ReviewThread);
        threadComment.ThreadResolved.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenAuthorTypenameIsBot_MapsAuthorIsBotTrue()
    {
        // Arrange
        string json = BuildThreeSurfaceJson(conversationCommentAuthorTypename: "Bot");
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.ShouldContain(c => c.Origin == CommentOrigin.Conversation);
        ProviderComment conversationComment = comments.Single(c => c.Origin == CommentOrigin.Conversation);
        conversationComment.AuthorIsBot.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenAuthorTypenameIsUser_MapsAuthorIsBotFalse()
    {
        // Arrange
        string json = BuildThreeSurfaceJson(conversationCommentAuthorTypename: "User");
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.ShouldContain(c => c.Origin == CommentOrigin.Conversation);
        ProviderComment conversationComment = comments.Single(c => c.Origin == CommentOrigin.Conversation);
        conversationComment.AuthorIsBot.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenReviewBodyIsNonEmpty_MapsToReviewSummaryWithSubmittedAtAsCreatedAt()
    {
        // Arrange
        string json = BuildThreeSurfaceJson(reviewBody: "Please fix this", reviewSubmittedAt: "2026-06-01T12:00:00Z");
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.ShouldContain(c => c.Origin == CommentOrigin.ReviewSummary);
        ProviderComment reviewSummary = comments.Single(c => c.Origin == CommentOrigin.ReviewSummary);
        reviewSummary.Body.ShouldBe("Please fix this");
        reviewSummary.CreatedAt.ShouldBe(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
        reviewSummary.FilePath.ShouldBeNull();
        reviewSummary.Line.ShouldBeNull();
    }

    [Fact]
    public async Task WhenReviewBodyIsEmpty_DoesNotEmitReviewSummary()
    {
        // Arrange
        string json = BuildThreeSurfaceJson(reviewBody: "");
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.ShouldNotContain(c => c.Origin == CommentOrigin.ReviewSummary);
    }

    [Fact]
    public async Task WhenConversationCommentExists_MapsToConversationOriginWithNoFilePath()
    {
        // Arrange
        string json = BuildThreeSurfaceJson(
            conversationCommentBody: "general comment",
            conversationCommentAuthorLogin: "alice");
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.ShouldContain(c => c.Origin == CommentOrigin.Conversation);
        ProviderComment conversationComment = comments.Single(c => c.Origin == CommentOrigin.Conversation);
        conversationComment.Body.ShouldBe("general comment");
        conversationComment.AuthorLogin.ShouldBe("alice");
        conversationComment.FilePath.ShouldBeNull();
        conversationComment.Line.ShouldBeNull();
        conversationComment.ThreadResolved.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenThreadCommentExists_MapsFilePathAndLine()
    {
        // Arrange
        string json = BuildThreeSurfaceJson(
            threadCommentBody: "inline comment",
            threadCommentPath: "src/Modules/Foo.cs",
            threadCommentLine: 10,
            threadCommentOriginalLine: 20);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.ShouldContain(c => c.Origin == CommentOrigin.ReviewThread);
        ProviderComment threadComment = comments.Single(c => c.Origin == CommentOrigin.ReviewThread);
        threadComment.FilePath.ShouldBe("src/Modules/Foo.cs");
        threadComment.Line.ShouldBe(10);
    }

    [Fact]
    public async Task WhenThreadCommentLineIsNull_UsesOriginalLine()
    {
        // Arrange
        string json = BuildThreeSurfaceJson(
            threadCommentPath: "src/Foo.cs",
            threadCommentLine: null,
            threadCommentOriginalLine: 42);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.ShouldContain(c => c.Origin == CommentOrigin.ReviewThread);
        ProviderComment threadComment = comments.Single(c => c.Origin == CommentOrigin.ReviewThread);
        threadComment.Line.ShouldBe(42);
    }

    [Fact]
    public async Task WhenSameCommentAppearsTwice_DeduplicatesIt()
    {
        // Arrange — same comment appears in both conversation comments and review threads.
        // The review-thread copy (with FilePath/Line) must be the one kept.
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "comments": {
                      "nodes": [
                        {
                          "databaseId": 999,
                          "body": "duplicate comment",
                          "createdAt": "2026-06-01T10:00:00Z",
                          "author": { "login": "alice", "__typename": "User" }
                        }
                      ]
                    },
                    "reviewThreads": {
                      "nodes": [
                        {
                          "isResolved": false,
                          "comments": {
                            "nodes": [
                              {
                                "databaseId": 999,
                                "body": "duplicate comment",
                                "path": "src/Foo.cs",
                                "line": 1,
                                "originalLine": null,
                                "createdAt": "2026-06-01T10:00:00Z",
                                "author": { "login": "alice", "__typename": "User" }
                              }
                            ]
                          }
                        }
                      ]
                    },
                    "reviews": {
                      "nodes": []
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.Count(c => c.Body == "duplicate comment").ShouldBe(1);
        ProviderComment kept = comments.Single(c => c.Body == "duplicate comment");
        kept.ShouldSatisfyAllConditions(
            () => kept.FilePath.ShouldBe("src/Foo.cs"),
            () => kept.Line.ShouldBe(1),
            () => kept.Origin.ShouldBe(CommentOrigin.ReviewThread));
    }

    [Fact]
    public async Task WhenTwoDistinctCommentsShareBodyTimeAndAuthor_BothAreKept()
    {
        // Arrange — two separate comments happen to have identical body, timestamp, and author.
        // Distinct databaseIds must prevent them from colliding in the de-dup set.
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
                          "body": "same body",
                          "createdAt": "2026-06-01T10:00:00Z",
                          "author": { "login": "alice", "__typename": "User" }
                        },
                        {
                          "databaseId": 102,
                          "body": "same body",
                          "createdAt": "2026-06-01T10:00:00Z",
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
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.Count(c => c.Body == "same body").ShouldBe(2);
    }

    [Fact]
    public async Task WhenCommentBodyExceeds4000Chars_TruncatesBodyWithSuffix()
    {
        // Arrange
        string longBody = new('x', 4100);
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "comments": {
                      "nodes": [
                        {
                          "body": "{{longBody}}",
                          "createdAt": "2026-06-01T10:00:00Z",
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
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        ProviderComment comment = comments[0];
        comment.Body.ShouldEndWith("[truncated]");
        comment.Body.Length.ShouldBeLessThanOrEqualTo(4000 + "[truncated]".Length);
    }

    [Fact]
    public async Task WhenFilePathContainsPathTraversal_SetsFilePathToNull()
    {
        // Arrange
        string json = BuildThreeSurfaceJson(
            threadCommentPath: "../../../etc/passwd",
            threadCommentLine: 1);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.ShouldContain(c => c.Origin == CommentOrigin.ReviewThread);
        ProviderComment threadComment = comments.Single(c => c.Origin == CommentOrigin.ReviewThread);
        threadComment.FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenThreadCommentHasOwnCreatedAt_MapsCreatedAtFromComment()
    {
        // Arrange
        string json = BuildThreeSurfaceJson(
            threadCommentCreatedAt: "2026-06-15T09:30:00Z");
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        comments.ShouldContain(c => c.Origin == CommentOrigin.ReviewThread);
        ProviderComment threadComment = comments.Single(c => c.Origin == CommentOrigin.ReviewThread);
        threadComment.CreatedAt.ShouldBe(new DateTimeOffset(2026, 6, 15, 9, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task WhenNullAuthor_MapsAuthorLoginAsEmptyAndIsNotBot()
    {
        // Arrange — null author represents deleted users
        string json = """
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "comments": {
                      "nodes": [
                        {
                          "body": "orphan comment",
                          "createdAt": "2026-06-01T10:00:00Z",
                          "author": null
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
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        ProviderComment comment = comments[0];
        comment.AuthorLogin.ShouldBe(string.Empty);
        comment.AuthorIsBot.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenThreadCommentBodyIsExactly4000Chars_BodyIsNotTruncated()
    {
        // Arrange — exactly 4000 characters should pass through unchanged
        string exactBody = new('x', 4000);
        string json = BuildThreeSurfaceJson(threadCommentBody: exactBody);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        ProviderComment threadComment = comments.Single(c => c.Origin == CommentOrigin.ReviewThread);
        threadComment.Body.Length.ShouldBe(4000);
        threadComment.Body.ShouldNotEndWith("[truncated]");
    }

    [Fact]
    public async Task WhenReviewBodyIsExactly4000Chars_BodyIsNotTruncated()
    {
        // Arrange — review-summary body at exactly 4000 characters should not be truncated
        string exactBody = new('x', 4000);
        string json = BuildThreeSurfaceJson(reviewBody: exactBody);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        ProviderComment reviewSummary = comments.Single(c => c.Origin == CommentOrigin.ReviewSummary);
        reviewSummary.Body.Length.ShouldBe(4000);
        reviewSummary.Body.ShouldNotEndWith("[truncated]");
    }

    [Fact]
    public async Task WhenThreadCommentPathIsAbsoluteUnixPath_SetsFilePathToNull()
    {
        // Arrange — a path starting with "/" is an absolute Unix path and must be sanitized to null
        string json = BuildThreeSurfaceJson(
            threadCommentPath: "/etc/passwd",
            threadCommentLine: 1);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        ProviderComment threadComment = comments.Single(c => c.Origin == CommentOrigin.ReviewThread);
        threadComment.FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenThreadCommentPathContainsDriveLetter_SetsFilePathToNull()
    {
        // Arrange — a Windows-style path with a drive-letter colon (C:/...) must be sanitized to null
        string json = BuildThreeSurfaceJson(
            threadCommentPath: "C:/windows/system32",
            threadCommentLine: 1);
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        IReadOnlyList<ProviderComment> comments = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>().Value;
        ProviderComment threadComment = comments.Single(c => c.Origin == CommentOrigin.ReviewThread);
        threadComment.FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenBaseUrlHasInvalidScheme_ReturnsInvalidBaseUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        GitHubHttpClient sut = BuildSut(handler);
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            invalidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<ProviderComment>>.Failure failure = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenPrUrlIsInvalid_ReturnsInvalidPullRequestUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/issues/1",
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<IReadOnlyList<ProviderComment>>.Failure failure = result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidPullRequestUrl");
    }
}
