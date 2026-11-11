using System.Net;

using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Comments;
using Foundry.Modules.Monitoring.Infrastructure;
using Foundry.Modules.Monitoring.Infrastructure.GitHub;
using Foundry.Modules.Monitoring.Infrastructure.RateBudget;
using Foundry.Shared;
using Foundry.Testing;

using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubHttpClientTests;

public sealed class PostIssueCommentAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";
    private const string CreatedJson = """{ "id": 1234567, "body": "Hello", "created_at": "2026-06-01T00:00:00Z" }""";

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(DelegatingHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    [Fact]
    public async Task WhenPostedToPullRequest_PostsToTheIssueCommentsEndpointOfThePullRequestNumber()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, CreatedJson);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.PostIssueCommentAsync(
            ValidBaseUrl,
            ValidSlug,
            new CommentThread.PullRequest("https://github.com/owner/repo/pull/17"),
            "Hello",
            ValidToken,
            CancellationToken.None);

        // Assert
        handler.ShouldSatisfyAllConditions(
            () => handler.LastRequest!.Method.ShouldBe(HttpMethod.Post),
            () => handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/issues/17/comments"),
            () => handler.LastRequest!.Headers.Authorization!.Parameter.ShouldBe(ValidToken),
            () => handler.LastRequestBody.ShouldBe("""{"body":"Hello"}"""));
    }

    [Fact]
    public async Task WhenPostedToIssue_PostsToTheIssueCommentsEndpoint()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, CreatedJson);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await sut.PostIssueCommentAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/issues/42/comments");
    }

    [Fact]
    public async Task WhenProviderCreatesTheComment_ReturnsItsId()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, CreatedJson);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostIssueCommentAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        result.ValueOrThrow().Value.ShouldBe(1234567);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task WhenProviderAnswersRetryableStatus_FailsTransient(HttpStatusCode statusCode)
    {
        // Arrange
        FakeHandler handler = new(statusCode, "{}");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostIssueCommentAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        CommentPostErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenForbiddenByRateLimit_FailsTransient()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Forbidden, """{ "message": "API rate limit exceeded" }""");
        handler.ResponseHeaders["X-RateLimit-Remaining"] = "0";
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostIssueCommentAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        CommentPostErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task WhenProviderRejectsTheRequest_FailsPermanent(HttpStatusCode statusCode)
    {
        // Arrange
        FakeHandler handler = new(statusCode, """{ "message": "nope" }""");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostIssueCommentAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(CommentPostErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenTheConnectionFails_FailsTransient()
    {
        // Arrange
        ThrowingHandler handler = new(new HttpRequestException("connection reset"));
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostIssueCommentAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        CommentPostErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheRequestTimesOut_FailsTransient()
    {
        // Arrange — HttpClient reports its timeout as a cancellation the caller did not request
        ThrowingHandler handler = new(new TaskCanceledException("timeout", new TimeoutException()));
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostIssueCommentAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        CommentPostErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheCallerCancels_Throws()
    {
        // Arrange
        using CancellationTokenSource cancellation = new();
        await cancellation.CancelAsync();
        ThrowingHandler handler = new(new OperationCanceledException(cancellation.Token));
        GitHubHttpClient sut = BuildSut(handler);

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(() => sut.PostIssueCommentAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, cancellation.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task WhenBodyIsBlank_FailsPermanentWithoutCallingProvider(string body)
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, CreatedJson);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostIssueCommentAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), body, ValidToken, CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(CommentPostErrors.PermanentCode),
            () => handler.AllRequests.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenPullRequestUrlHasNoNumber_FailsPermanentWithoutCallingProvider()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, CreatedJson);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostIssueCommentAsync(
            ValidBaseUrl,
            ValidSlug,
            new CommentThread.PullRequest("https://github.com/owner/repo"),
            "Hello",
            ValidToken,
            CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(CommentPostErrors.PermanentCode),
            () => handler.AllRequests.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenResponseCarriesNoId_FailsPermanent()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.Created, """{ "body": "Hello" }""");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<ProviderCommentId> result = await sut.PostIssueCommentAsync(
            ValidBaseUrl, ValidSlug, new CommentThread.Issue(42), "Hello", ValidToken, CancellationToken.None);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(CommentPostErrors.PermanentCode);
    }
}
