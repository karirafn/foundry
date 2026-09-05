using System.Net;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;
using Foundry.UnitTests.Modules.Monitoring.Infrastructure;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class GetPullRequestReviewFeedbackAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidMrUrl = "https://gitlab.com/group/project/-/merge_requests/1";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(DelegatingHandler handler)
    {
        HttpClient httpClient = new(handler);
        return new GitLabHttpClient(
            httpClient,
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
    }

    private static string BuildNoteJson(
        string body = "Fix this issue",
        bool resolvable = true,
        bool resolved = false,
        bool system = false,
        string? newPath = null,
        int? newLine = null,
        string createdAt = "2026-06-01T00:00:00Z",
        string updatedAt = "2026-06-01T00:00:00Z",
        string authorUsername = "alice")
    {
        string positionJson;
        if (newPath is null && newLine is null)
        {
            positionJson = "null";
        }
        else
        {
            string lineValue = newLine.HasValue
                ? newLine.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "null";
            string pathValue = newPath is null ? "null" : $"\"{newPath}\"";
            positionJson = $"{{\"new_path\":{pathValue},\"new_line\":{lineValue}}}";
        }

        return $$"""
            {
              "body": "{{body}}",
              "resolvable": {{resolvable.ToString().ToLowerInvariant()}},
              "resolved": {{resolved.ToString().ToLowerInvariant()}},
              "system": {{system.ToString().ToLowerInvariant()}},
              "created_at": "{{createdAt}}",
              "updated_at": "{{updatedAt}}",
              "author": { "username": "{{authorUsername}}" },
              "position": {{positionJson}}
            }
            """;
    }

    private static string BuildDiscussionJson(params string[] noteJsons)
    {
        string notes = string.Join(",", noteJsons);
        return $"[{{\"notes\":[{notes}]}}]";
    }

    private static string BuildDiscussionsJson(params string[] discussionJsons)
    {
        string discussions = string.Join(",", discussionJsons.Select(d => $"{{\"notes\":[{d}]}}"));
        return $"[{discussions}]";
    }

    // --- TDD Cycle 1: multi-note discussion maps every note, not just Notes[0] ---

    [Fact]
    public async Task WhenDiscussionHasMultipleNotes_ReturnsAllNotesMapped()
    {
        // Arrange — one discussion with a note + a reply → two ProviderComments
        string note1 = BuildNoteJson(body: "Original comment");
        string note2 = BuildNoteJson(body: "Reply to comment");
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(note1, note2));
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value.Count.ShouldBe(2);
        success.Value.ShouldContain(c => c.Body == "Original comment");
        success.Value.ShouldContain(c => c.Body == "Reply to comment");
    }

    // --- TDD Cycle 2: system note maps IsSystem = true ---

    [Fact]
    public async Task WhenNoteHasSystemTrue_MapsIsSystemToTrue()
    {
        // Arrange
        string noteJson = BuildNoteJson(body: "System event note", system: true);
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value.Count.ShouldBe(1);
        success.Value[0].IsSystem.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenNoteHasSystemFalse_MapsIsSystemToFalse()
    {
        // Arrange
        string noteJson = BuildNoteJson(body: "Human comment", system: false);
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].IsSystem.ShouldBeFalse();
    }

    // --- TDD Cycle 3: created_at populates CreatedAt; updated_at different from created_at still uses created_at ---

    [Fact]
    public async Task WhenNoteHasCreatedAt_MapsCreatedAtFromCreatedAt()
    {
        // Arrange
        string noteJson = BuildNoteJson(
            createdAt: "2026-03-15T10:00:00Z",
            updatedAt: "2026-03-15T10:00:00Z");
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        DateTimeOffset expectedCreatedAt = new(2026, 3, 15, 10, 0, 0, TimeSpan.Zero);
        success.Value[0].CreatedAt.ShouldBe(expectedCreatedAt);
    }

    [Fact]
    public async Task WhenNoteUpdatedAtDiffersFromCreatedAt_CreatedAtIsUsedNotUpdatedAt()
    {
        // Arrange — created_at is old; updated_at is recent; CreatedAt must use created_at
        string noteJson = BuildNoteJson(
            createdAt: "2025-01-01T00:00:00Z",
            updatedAt: "2026-06-01T00:00:00Z");
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        DateTimeOffset expectedCreatedAt = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        success.Value[0].CreatedAt.ShouldBe(expectedCreatedAt);
    }

    // --- TDD Cycle 4: non-resolvable discussion → notes returned as Conversation, ThreadResolved = false
    //     (inverted from old WhenDiscussionIsNotResolvable_SkipsIt) ---

    [Fact]
    public async Task WhenDiscussionIsNotResolvable_ReturnsItsNotes()
    {
        // Arrange — non-resolvable discussion (e.g. a plain conversation comment)
        string noteJson = BuildNoteJson(resolvable: false, resolved: false, body: "Plain comment");
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value.Count.ShouldBe(1);
        success.Value[0].ShouldSatisfyAllConditions(
            () => success.Value[0].Body.ShouldBe("Plain comment"),
            () => success.Value[0].Origin.ShouldBe(CommentOrigin.Conversation),
            () => success.Value[0].ThreadResolved.ShouldBeFalse());
    }

    // --- TDD Cycle 5: resolvable+resolved discussion maps ThreadResolved = true ---

    [Fact]
    public async Task WhenDiscussionIsResolvableAndResolved_MapsThreadResolvedToTrue()
    {
        // Arrange
        string noteJson = BuildNoteJson(resolvable: true, resolved: true);
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value.Count.ShouldBe(1);
        success.Value[0].ThreadResolved.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenDiscussionIsResolvableAndUnresolved_MapsThreadResolvedToFalse()
    {
        // Arrange
        string noteJson = BuildNoteJson(resolvable: true, resolved: false);
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].ThreadResolved.ShouldBeFalse();
    }

    [Fact]
    public async Task WhenDiscussionIsResolvable_MapsOriginToReviewThread()
    {
        // Arrange
        string noteJson = BuildNoteJson(resolvable: true, resolved: false);
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        using HttpClient httpClient = new(handler);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].Origin.ShouldBe(CommentOrigin.ReviewThread);
    }

    // --- TDD Cycle 6: pagination — per_page=100, multiple pages, early termination ---

    [Fact]
    public async Task WhenCalled_RequestIncludesPerPage100()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> _ = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        HttpRequestMessage request = handler.LastRequest.ShouldNotBeNull();
        request.RequestUri.ShouldNotBeNull();
        request.RequestUri.Query.ShouldContain("per_page=100");
    }

    [Fact]
    public async Task WhenFirstPageHasFewerThan100Items_DoesNotRequestSecondPage()
    {
        // Arrange — single page with 3 notes (< 100), should not fetch page 2
        string noteJson = BuildNoteJson();
        string page1 = BuildDiscussionJson(noteJson, noteJson, noteJson);
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, page1),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value.Count.ShouldBe(3);
        handler.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenFirstPageHas100Items_RequestsSecondPage()
    {
        // Arrange — full first page (100 discussions, each with 1 note), then empty page 2
        string fullPage = BuildDiscussionPageJson(100);
        string emptyPage = "[]";

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, fullPage),
            (HttpStatusCode.OK, emptyPage),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value.Count.ShouldBe(100);
        handler.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task WhenSecondPageHasFewerThan100Items_TerminatesWithoutThirdPage()
    {
        // Arrange — page1=100, page2=42 → no page3 request
        string page1 = BuildDiscussionPageJson(100);
        string page2 = BuildDiscussionPageJson(42);

        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, page1),
            (HttpStatusCode.OK, page2),
        ]);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value.Count.ShouldBe(142);
        handler.Requests.Count.ShouldBe(2);
    }

    // --- Additional mapping tests: author, file path, body truncation, guards ---

    [Fact]
    public async Task WhenNoteHasAuthor_MapsAuthorLogin()
    {
        // Arrange
        string noteJson = BuildNoteJson(authorUsername: "bob");
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].AuthorLogin.ShouldBe("bob");
    }

    [Fact]
    public async Task WhenNoteHasPosition_MapsFilePath()
    {
        // Arrange
        string noteJson = BuildNoteJson(newPath: "src/Foo.cs");
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].FilePath.ShouldBe("src/Foo.cs");
    }

    [Fact]
    public async Task WhenPositionIsNull_FilePathIsNull()
    {
        // Arrange
        string noteJson = BuildNoteJson(body: "Comment without path");
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathContainsPathTraversal_SetsFilePathToNull()
    {
        // Arrange
        string noteJson = BuildNoteJson(newPath: "../../../etc/passwd");
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathStartsWithSlash_SetsFilePathToNull()
    {
        // Arrange
        string noteJson = BuildNoteJson(newPath: "/etc/passwd");
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathContainsColon_SetsFilePathToNull()
    {
        // Arrange
        string noteJson = BuildNoteJson(newPath: "C:/windows/system32");
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathExceedsMaxLength_SetsFilePathToNull()
    {
        // Arrange
        string longPath = new('a', 4097);
        string noteJson = BuildNoteJson(newPath: longPath);
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].FilePath.ShouldBeNull();
    }

    [Fact]
    public async Task WhenFilePathIsExactlyMaxLength_RetainsFilePath()
    {
        // Arrange
        string maxPath = new('a', 4096);
        string noteJson = BuildNoteJson(newPath: maxPath);
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].FilePath.ShouldBe(maxPath);
    }

    [Fact]
    public async Task WhenCommentBodyExceeds4000Chars_TruncatesBodyWithSuffix()
    {
        // Arrange
        string longBody = new('x', 4100);
        string noteJson = BuildNoteJson(body: longBody);
        FakeHandler handler = new(HttpStatusCode.OK, BuildDiscussionJson(noteJson));
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Success success =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Success>();
        success.Value[0].Body.ShouldEndWith("[truncated]");
        success.Value[0].Body.Length.ShouldBeLessThanOrEqualTo(4000 + "[truncated]".Length);
    }

    [Fact]
    public async Task WhenMrUrlCannotBeParsed_ReturnsInvalidUrlError()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            ValidBaseUrl,
            ValidSlug,
            "https://gitlab.com/group/project/-/issues/1",
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidMergeRequestUrl");
    }

    [Fact]
    public async Task WhenBaseUrlHasNonHttpsScheme_ReturnsFailure()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "[]");
        GitLabHttpClient sut = BuildSut(handler);
        Uri invalidBaseUrl = new("ftp://gitlab.com/api/v4");

        // Act
        Result<IReadOnlyList<ProviderComment>> result = await sut.GetPullRequestReviewFeedbackAsync(
            invalidBaseUrl,
            ValidSlug,
            ValidMrUrl,
            "glpat_token",
            CancellationToken.None);

        // Assert
        Result<IReadOnlyList<ProviderComment>>.Failure failure =
            result.ShouldBeOfType<Result<IReadOnlyList<ProviderComment>>.Failure>();
        failure.Error.Code.ShouldBe("GitLab.InvalidBaseUrl");
    }

    // --- Helper: build a page of N single-note discussions for pagination tests ---

    private static string BuildDiscussionPageJson(int count, int startIndex = 0)
    {
        IEnumerable<string> discussions = Enumerable
            .Range(startIndex, count)
            .Select(i => $"{{\"notes\":[{{\"body\":\"Comment {i}\",\"resolvable\":true,\"resolved\":false,\"system\":false,\"created_at\":\"2026-06-01T00:00:00Z\",\"updated_at\":\"2026-06-01T00:00:00Z\",\"author\":{{\"username\":\"user{i}\"}},\"position\":null}}]}}");
        return $"[{string.Join(",", discussions)}]";
    }
}
