using System.Net;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
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
    private static readonly DateTimeOffset Since = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private const string ValidPrUrl = "https://github.com/owner/repo/pull/1";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))), new InMemoryProviderRateBudget(), TimeProvider.System);

    private static string BuildReviewFeedbackJson(
        string reviewBody = "",
        string submittedAt = "2026-06-01T00:00:00Z",
        IReadOnlyList<(string Body, string? Path, int? Line, int? OriginalLine)>? comments = null)
    {
        comments ??= [];
        string commentsJson = string.Join(
            ",",
            comments.Select(c =>
            {
                string lineVal = c.Line.HasValue
                    ? c.Line.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "null";
                string origLineVal = c.OriginalLine.HasValue
                    ? c.OriginalLine.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : "null";
                string pathVal = c.Path is not null ? $"\"{c.Path}\"" : "null";
                return $$"""{"body":"{{c.Body}}","path":{{pathVal}},"line":{{lineVal}},"originalLine":{{origLineVal}}}""";
            }));

        return $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "{{reviewBody}}",
                          "submittedAt": "{{submittedAt}}",
                          "comments": {
                            "nodes": [{{commentsJson}}]
                          }
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
        string json = BuildReviewFeedbackJson();
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
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
    public async Task WhenCalled_RequestBodyContainsPrReviewFeedbackQuery()
    {
        // Arrange
        string json = BuildReviewFeedbackJson();
        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        string body = handler.LastRequestBody.ShouldNotBeNull();
        body.ShouldContain("rateLimit");
        body.ShouldContain("CHANGES_REQUESTED");
        body.ShouldContain("reviews");
        body.ShouldContain("submittedAt");
        body.ShouldContain("originalLine");
    }

    [Fact]
    public async Task WhenMoreThan50CommentsExist_ReturnsOnly50Comments()
    {
        // Arrange
        // Build a GraphQL response with 51 comments in one review — expect only 50 returned
        StringBuilder commentsBuilder = new("[");
        for (int i = 0; i < 51; i++)
        {
            if (i > 0) { commentsBuilder.Append(','); }
            commentsBuilder.Append(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{{\"body\":\"Comment {i}\",\"path\":\"src/Foo.cs\",\"line\":null,\"originalLine\":{i + 1}}}");
        }
        commentsBuilder.Append(']');

        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "",
                          "submittedAt": "2026-06-01T00:00:00Z",
                          "comments": { "nodes": {{commentsBuilder}} }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.Count.ShouldBe(50);
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
                    "reviews": {
                      "nodes": [
                        {
                          "body": "",
                          "submittedAt": "2026-06-01T00:00:00Z",
                          "comments": {
                            "nodes": [
                              { "body": "{{longBody}}", "path": "src/Foo.cs", "line": null, "originalLine": 1 }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        ReviewComment comment = success.Value.Comments[0];
        comment.Body.ShouldEndWith("[truncated]");
        comment.Body.Length.ShouldBeLessThanOrEqualTo(4000 + "[truncated]".Length);
    }

    [Fact]
    public async Task WhenCommentBodyIsExactly4000Chars_DoesNotTruncate()
    {
        // Arrange
        string exactBody = new('x', 4000);
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "",
                          "submittedAt": "2026-06-01T00:00:00Z",
                          "comments": {
                            "nodes": [
                              { "body": "{{exactBody}}", "path": "src/Foo.cs", "line": null, "originalLine": 1 }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].Body.ShouldBe(exactBody);
    }

    [Fact]
    public async Task WhenReviewBodyExceeds4000Chars_TruncatesReviewBodyWithSuffix()
    {
        // Arrange
        string longBody = new('y', 5000);
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "{{longBody}}",
                          "submittedAt": "2026-06-01T00:00:00Z",
                          "comments": { "nodes": [] }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].Body.ShouldEndWith("[truncated]");
    }

    [Fact]
    public async Task WhenFilePathContainsPathTraversal_SetsFilePathToNull()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "",
                          "submittedAt": "2026-06-01T00:00:00Z",
                          "comments": {
                            "nodes": [
                              { "body": "Fix this", "path": "../../../etc/passwd", "line": null, "originalLine": 1 }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathIsAbsoluteUnixPath_SetsFilePathToNull()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "",
                          "submittedAt": "2026-06-01T00:00:00Z",
                          "comments": {
                            "nodes": [
                              { "body": "Fix this", "path": "/absolute/path/to/file.cs", "line": null, "originalLine": 1 }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathContainsDriveLetterColon_SetsFilePathToNull()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "",
                          "submittedAt": "2026-06-01T00:00:00Z",
                          "comments": {
                            "nodes": [
                              { "body": "Fix this", "path": "C:\\Windows\\System32\\file.cs", "line": null, "originalLine": 1 }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathIsValidRelativePath_PreservesFilePath()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "",
                          "submittedAt": "2026-06-01T00:00:00Z",
                          "comments": {
                            "nodes": [
                              { "body": "Fix this", "path": "src/Modules/Workers/Features/SystemPromptBuilder.cs", "line": null, "originalLine": 42 }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBe("src/Modules/Workers/Features/SystemPromptBuilder.cs");
    }

    [Fact]
    public async Task WhenCommentLineIsNull_UsesOriginalLine()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "",
                          "submittedAt": "2026-06-01T00:00:00Z",
                          "comments": {
                            "nodes": [
                              { "body": "Fix this", "path": "src/Foo.cs", "line": null, "originalLine": 42 }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].Line.ShouldBe(42);
    }

    [Fact]
    public async Task WhenCommentLineIsSet_UsesLine()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "",
                          "submittedAt": "2026-06-01T00:00:00Z",
                          "comments": {
                            "nodes": [
                              { "body": "Fix this", "path": "src/Foo.cs", "line": 10, "originalLine": 42 }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].Line.ShouldBe(10);
    }

    [Fact]
    public async Task WhenReviewSubmittedAtIsAtOrBeforeSince_FiltersOutReview()
    {
        // Arrange — review submitted exactly at "since", must be filtered (need strictly after)
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "Old review",
                          "submittedAt": "2026-01-01T00:00:00Z",
                          "comments": {
                            "nodes": [
                              { "body": "Old comment", "path": "src/Foo.cs", "line": null, "originalLine": 1 }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenReviewSubmittedAtIsAfterSince_IncludesReview()
    {
        // Arrange
        string json = $$"""
            {
              "data": {
                "rateLimit": { "cost": 1, "remaining": 4999 },
                "repository": {
                  "pullRequest": {
                    "reviews": {
                      "nodes": [
                        {
                          "body": "",
                          "submittedAt": "2026-06-01T00:00:00Z",
                          "comments": {
                            "nodes": [
                              { "body": "New comment", "path": "src/Foo.cs", "line": null, "originalLine": 1 }
                            ]
                          }
                        }
                      ]
                    }
                  }
                }
              }
            }
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.Count.ShouldBe(1);
        success.Value.Comments[0].Body.ShouldBe("New comment");
    }

    [Fact]
    public async Task WhenBaseUrlHasInvalidScheme_ReturnsInvalidBaseUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        GitHubHttpClient sut = BuildSut(handler);
        Uri invalidBaseUrl = new("ftp://api.github.com");

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            invalidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidPrUrl,
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<ReviewFeedback>.Failure failure = result.ShouldBeOfType<Result<ReviewFeedback>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Fact]
    public async Task WhenPrUrlIsInvalid_ReturnsInvalidPullRequestUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://github.com/owner/repo/issues/1",
            since: Since,
            token: "ghp_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<ReviewFeedback>.Failure failure = result.ShouldBeOfType<Result<ReviewFeedback>.Failure>();
        failure.Error.Code.ShouldBe("GitHub.InvalidPullRequestUrl");
    }
}
