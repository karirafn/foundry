using System.Net;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

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

    private static string BuildReviewsJson(
        long id = 1,
        string state = "CHANGES_REQUESTED",
        string body = "",
        string submittedAt = "2026-06-01T00:00:00Z")
    {
        return $$"""
            [
              {
                "id": {{id}},
                "state": "{{state}}",
                "body": "{{body}}",
                "submitted_at": "{{submittedAt}}"
              }
            ]
            """;
    }

    private static string BuildFileCommentsJson(
        string commentBody = "Fix this",
        string path = "src/Foo.cs",
        int? line = null,
        int? originalLine = 10)
    {
        string lineValue = line.HasValue ? line.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "null";
        string originalLineValue = originalLine.HasValue
            ? originalLine.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "null";
        return $$"""
            [
              {
                "body": "{{commentBody}}",
                "path": "{{path}}",
                "line": {{lineValue}},
                "original_line": {{originalLineValue}}
              }
            ]
            """;
    }

    [Fact]
    public async Task WhenMoreThan50CommentsExist_ReturnsOnly50Comments()
    {
        // Arrange
        // Build a reviews response with a single CHANGES_REQUESTED review
        // and 51 inline comments — expect only 50 to be returned
        string reviewsJson = BuildReviewsJson(id: 1);
        StringBuilder commentsJsonBuilder = new("[");
        for (int i = 0; i < 51; i++)
        {
            if (i > 0) { commentsJsonBuilder.Append(','); }
            commentsJsonBuilder.Append(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{{\"body\":\"Comment {i}\",\"path\":\"src/Foo.cs\",\"line\":null,\"original_line\":{i + 1}}}");
        }
        commentsJsonBuilder.Append(']');

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, reviewsJson),
            (HttpStatusCode.OK, commentsJsonBuilder.ToString()),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

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
        string reviewsJson = BuildReviewsJson(id: 1);
        string commentsJson = $$"""
            [
              {
                "body": "{{longBody}}",
                "path": "src/Foo.cs",
                "line": null,
                "original_line": 1
              }
            ]
            """;

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, reviewsJson),
            (HttpStatusCode.OK, commentsJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

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
        string reviewsJson = BuildReviewsJson(id: 1);
        string commentsJson = $$"""
            [
              {
                "body": "{{exactBody}}",
                "path": "src/Foo.cs",
                "line": null,
                "original_line": 1
              }
            ]
            """;

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, reviewsJson),
            (HttpStatusCode.OK, commentsJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

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
        string reviewsJson = $$"""
            [
              {
                "id": 1,
                "state": "CHANGES_REQUESTED",
                "body": "{{longBody}}",
                "submitted_at": "2026-06-01T00:00:00Z"
              }
            ]
            """;

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, reviewsJson),
            (HttpStatusCode.OK, "[]"),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

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
        string reviewsJson = BuildReviewsJson(id: 1);
        string commentsJson = """
            [
              {
                "body": "Fix this",
                "path": "../../../etc/passwd",
                "line": null,
                "original_line": 1
              }
            ]
            """;

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, reviewsJson),
            (HttpStatusCode.OK, commentsJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

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
        string reviewsJson = BuildReviewsJson(id: 1);
        string commentsJson = """
            [
              {
                "body": "Fix this",
                "path": "/absolute/path/to/file.cs",
                "line": null,
                "original_line": 1
              }
            ]
            """;

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, reviewsJson),
            (HttpStatusCode.OK, commentsJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

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
        string reviewsJson = BuildReviewsJson(id: 1);
        string commentsJson = """
            [
              {
                "body": "Fix this",
                "path": "C:\\Windows\\System32\\file.cs",
                "line": null,
                "original_line": 1
              }
            ]
            """;

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, reviewsJson),
            (HttpStatusCode.OK, commentsJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

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
        string reviewsJson = BuildReviewsJson(id: 1);
        string commentsJson = """
            [
              {
                "body": "Fix this",
                "path": "src/Modules/Workers/Features/SystemPromptBuilder.cs",
                "line": null,
                "original_line": 42
              }
            ]
            """;

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, reviewsJson),
            (HttpStatusCode.OK, commentsJson),
        ]);
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

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
    public async Task WhenBaseUrlHasInvalidScheme_ReturnsInvalidBaseUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);
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
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitHubHttpClient sut = new(httpClient);

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
