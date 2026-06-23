using System.Net;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetPullRequestReviewFeedbackAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidMrUrl = "https://gitlab.com/group/project/-/merge_requests/1";
    private static readonly DateTimeOffset EpochSince = DateTimeOffset.MinValue;
    private static readonly DateTimeOffset RecentSince = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static string BuildNoteJson(
        string body = "Fix this issue",
        bool resolvable = true,
        bool resolved = false,
        string? newPath = null,
        string updatedAt = "2026-06-01T00:00:00Z")
    {
        string position = newPath is null
            ? "null"
            : $$"""{ "new_path": "{{newPath}}" }""";

        return $$"""
            {
              "body": "{{body}}",
              "resolvable": {{resolvable.ToString().ToLowerInvariant()}},
              "resolved": {{resolved.ToString().ToLowerInvariant()}},
              "updated_at": "{{updatedAt}}",
              "position": {{position}}
            }
            """;
    }

    private static string BuildDiscussionJson(string noteJson) =>
        $$"""[{"notes":[{{noteJson}}]}]""";

    [Fact]
    public async Task WhenMrHasUnresolvedDiscussions_ReturnsComments()
    {
        // Arrange
        string json = BuildDiscussionJson(BuildNoteJson(body: "Fix this issue", newPath: "src/Foo.cs"));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
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
        string json = BuildDiscussionJson(BuildNoteJson(resolved: true));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
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
        string json = BuildDiscussionJson(BuildNoteJson(resolvable: false));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoteUpdatedAtIsBeforeSince_SkipsIt()
    {
        // Arrange
        string json = BuildDiscussionJson(BuildNoteJson(updatedAt: "2025-12-31T23:59:59Z"));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            RecentSince,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoteUpdatedAtIsEqualToSince_SkipsIt()
    {
        // Arrange — updated_at exactly at the since boundary is excluded (not strictly after)
        string json = BuildDiscussionJson(BuildNoteJson(updatedAt: "2026-01-01T00:00:00Z"));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            RecentSince,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task WhenNoteUpdatedAtIsAfterSince_IncludesIt()
    {
        // Arrange
        string json = BuildDiscussionJson(BuildNoteJson(body: "New comment", updatedAt: "2026-01-02T00:00:00Z"));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            RecentSince,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments.Count.ShouldBe(1);
        success.Value.Comments[0].Body.ShouldBe("New comment");
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
                $$"""{"notes":[{"body":"Comment {{i}}","resolvable":true,"resolved":false,"updated_at":"2026-06-01T00:00:00Z","position":null}]}""");
        }
        jsonBuilder.Append(']');

        FakeHandler handler = new(HttpStatusCode.OK, jsonBuilder.ToString());
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
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
        string json = BuildDiscussionJson(BuildNoteJson(body: longBody));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
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
        string json = BuildDiscussionJson(BuildNoteJson(body: "Comment without path"));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
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
        string json = BuildDiscussionJson(BuildNoteJson(body: "Fix this", newPath: "../../../etc/passwd"));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathStartsWithSlash_SetsFilePathToNull()
    {
        // Arrange
        string json = BuildDiscussionJson(BuildNoteJson(body: "Fix this", newPath: "/etc/passwd"));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathContainsColon_SetsFilePathToNull()
    {
        // Arrange
        string json = BuildDiscussionJson(BuildNoteJson(body: "Fix this", newPath: "C:/windows/system32"));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathExceedsMaxLength_SetsFilePathToNull()
    {
        // Arrange
        string longPath = new('a', 4097);
        string json = BuildDiscussionJson(BuildNoteJson(body: "Fix this", newPath: longPath));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathIsExactlyMaxLength_RetainsFilePath()
    {
        // Arrange
        string maxPath = new('a', 4096);
        string json = BuildDiscussionJson(BuildNoteJson(body: "Fix this", newPath: maxPath));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBe(maxPath);
    }

    [Fact]
    public async Task WhenFilePathIsEmpty_RetainsFilePath()
    {
        // Arrange
        string json = BuildDiscussionJson(BuildNoteJson(body: "Fix this", newPath: ""));
        FakeHandler handler = new(HttpStatusCode.OK, json);
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = new(httpClient);

        // Act
        Result<ReviewFeedback> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            EpochSince,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        Result<ReviewFeedback>.Success success = result.ShouldBeOfType<Result<ReviewFeedback>.Success>();
        success.Value.Comments[0].FilePath.ShouldBe("");
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
            "https://gitlab.com/group/project/-/issues/1",
            EpochSince,
            "glpat_token",
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
            ValidMrUrl,
            EpochSince,
            "glpat_token",
            CancellationToken.None);

        // Assert
        result.IsFailure.ShouldBeTrue();
        Result<ReviewFeedback>.Failure failure = result.ShouldBeOfType<Result<ReviewFeedback>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }
}
