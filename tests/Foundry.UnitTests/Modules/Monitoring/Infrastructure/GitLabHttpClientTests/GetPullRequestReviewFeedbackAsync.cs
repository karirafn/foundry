using System.Net;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetPullRequestReviewFeedbackAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidMrUrl = "https://gitlab.com/group/project/-/merge_requests/1";

    private static RepositorySlug ValidSlug =>
        ((Result<RepositorySlug>.Success)RepositorySlug.Create("group/project")).Value;

    [Fact]
    public async Task WhenMrHasUnresolvedDiscussions_ReturnsComments()
    {
        // Arrange
        string json = """
            [
              {
                "notes": [
                  {
                    "body": "Fix this issue",
                    "resolvable": true,
                    "resolved": false,
                    "position": { "new_path": "src/Foo.cs" }
                  }
                ]
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.Count.ShouldBe(1);
        ReviewComment comment = success.Value.Comments[0];
        comment.ShouldSatisfyAllConditions(
            () => comment.Body.ShouldBe("Fix this issue"),
            () => comment.FilePath.ShouldBe("src/Foo.cs"));
    }

    [Fact]
    public async Task WhenDiscussionIsResolved_SkipsIt()
    {
        // Arrange
        string json = """
            [
              {
                "notes": [
                  {
                    "body": "Already fixed",
                    "resolvable": true,
                    "resolved": true,
                    "position": null
                  }
                ]
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenDiscussionIsNotResolvable_SkipsIt()
    {
        // Arrange
        string json = """
            [
              {
                "notes": [
                  {
                    "body": "General comment",
                    "resolvable": false,
                    "resolved": false,
                    "position": null
                  }
                ]
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenMoreThan50UnresolvedDiscussions_ReturnsOnly50Comments()
    {
        // Arrange
        StringBuilder jsonBuilder = new("[");
        for (int i = 0; i < 51; i++)
        {
            if (i > 0) { jsonBuilder.Append(','); }
            jsonBuilder.Append(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{{\"notes\":[{{\"body\":\"Comment {i}\",\"resolvable\":true,\"resolved\":false,\"position\":null}}]}}");
        }
        jsonBuilder.Append(']');

        FakeHandler handler = new(HttpStatusCode.OK, jsonBuilder.ToString());
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
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
            [
              {
                "notes": [
                  {
                    "body": "{{longBody}}",
                    "resolvable": true,
                    "resolved": false,
                    "position": null
                  }
                ]
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        ReviewComment comment = success.Value.Comments[0];
        comment.Body.ShouldEndWith("[truncated]");
        comment.Body.Length.ShouldBeLessThanOrEqualTo(4000 + "[truncated]".Length);
    }

    [Fact]
    public async Task WhenPositionIsNull_FilePathIsNull()
    {
        // Arrange
        string json = """
            [
              {
                "notes": [
                  {
                    "body": "Comment without path",
                    "resolvable": true,
                    "resolved": false,
                    "position": null
                  }
                ]
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathContainsPathTraversal_SetsFilePathToNull()
    {
        // Arrange
        string json = """
            [
              {
                "notes": [
                  {
                    "body": "Fix this",
                    "resolvable": true,
                    "resolved": false,
                    "position": { "new_path": "../../../etc/passwd" }
                  }
                ]
              }
            ]
            """;

        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenMrUrlCannotBeParsed_ReturnsInvalidUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            pullRequestUrl: "https://gitlab.com/group/project/-/issues/1",
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<ReviewFeedback>.Failure failure = result.ShouldBeOfType<Result<ReviewFeedback>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidMergeRequestUrl");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            invalidBaseUrl,
            ValidSlug,
            pullRequestUrl: ValidMrUrl,
            token: "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<ReviewFeedback>.Failure failure = result.ShouldBeOfType<Result<ReviewFeedback>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }
}
