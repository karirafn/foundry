using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitLab;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitLabHttpClientTests;

public sealed class PostNoteAsync
{
    private static readonly Uri ValidBaseUrl = new("https://gitlab.com/api/v4");
    private const string ValidToken = "glpat-token";
    private const string CreatedJson = """{ "id": 302, "body": "Hello", "system": false, "created_at": "2026-06-01T00:00:00Z" }""";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(DelegatingHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenPostedToMergeRequest_PostsToTheMergeRequestNotesEndpoint()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, CreatedJson);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await sut.PostNoteAsync(
            ValidBaseUrl,
            ValidSlug,
            new CommentThread.PullRequest("https://gitlab.com/group/project/-/merge_requests/5"),
            "Hello",
            ValidToken,
            CancellationToken.None);

        // Assert
        handler.ShouldSatisfyAllConditions(
            () => handler.LastRequest!.Method.ShouldBe(HttpMethod.Post),
            () => handler.LastRequest!.RequestUri!.AbsoluteUri
                .ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/merge_requests/5/notes"),
            () => handler.LastRequestBody.ShouldBe("""{"body":"Hello"}"""));
    }

    [Fact]
    public async Task WhenPostedToIssue_PostsToTheIssueNotesEndpoint()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, CreatedJson);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        await sut.PostNoteAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        handler.LastRequest!.RequestUri!.AbsoluteUri
            .ShouldBe("https://gitlab.com/api/v4/projects/group%2Fproject/issues/42/notes");
    }

    [Fact]
    public async Task WhenProviderCreatesTheNote_ReturnsItsId()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, CreatedJson);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostNoteAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        result.ValueOrThrow().Value.ShouldBe(302);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.GatewayTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task WhenProviderAnswersRetryableStatus_FailsTransient(HttpStatusCode statusCode)
    {
        // Arrange
        FakeHandler handler = new(statusCode, "{}");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostNoteAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        CommentPostErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenForbiddenByRateLimit_FailsTransient()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, "{}");
        handler.ResponseHeaders["RateLimit-Remaining"] = "0";
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostNoteAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        CommentPostErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task WhenProviderRejectsTheRequest_FailsPermanent(HttpStatusCode statusCode)
    {
        // Arrange
        FakeHandler handler = new(statusCode, """{ "message": "nope" }""");
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostNoteAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(CommentPostErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenTheConnectionFails_FailsTransient()
    {
        // Arrange
        ThrowingHandler handler = new(new HttpRequestException("connection refused"));
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostNoteAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        CommentPostErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenMergeRequestUrlHasNoIid_FailsPermanentWithoutCallingProvider()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, CreatedJson);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostNoteAsync(
            ValidBaseUrl,
            ValidSlug,
            new CommentThread.PullRequest("https://gitlab.com/group/project"),
            "Hello",
            ValidToken,
            CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(CommentPostErrors.PermanentCode),
            () => handler.AllRequests.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenBodyIsBlank_FailsPermanentWithoutCallingProvider()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, CreatedJson);
        GitLabHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostNoteAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), " ", ValidToken, CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(CommentPostErrors.PermanentCode),
            () => handler.AllRequests.ShouldBeEmpty());
    }
}
