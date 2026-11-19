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

public sealed class PostPullRequestReviewAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/17";
    private const string Commit = "ecdd80bb57125d7ba9641ffaa4d7d2c19d3f3091";

    // The example response of GitHub's REST reference "Create a review for a pull request" (API version 2022-11-28),
    // trimmed to the fields it documents for the review itself. No captured response from a live call was available.
    private const string CreatedJson = """
        {
          "id": 80,
          "node_id": "MDE3OlB1bGxSZXF1ZXN0UmV2aWV3ODA=",
          "user": { "login": "octocat", "id": 1, "type": "User", "site_admin": false },
          "body": "This is close to perfect! Please address the suggested inline change.",
          "state": "CHANGES_REQUESTED",
          "html_url": "https://github.com/octocat/Hello-World/pull/12#pullrequestreview-80",
          "pull_request_url": "https://api.github.com/repos/octocat/Hello-World/pulls/12",
          "submitted_at": "2019-11-17T17:43:43Z",
          "commit_id": "ecdd80bb57125d7ba9641ffaa4d7d2c19d3f3091",
          "author_association": "COLLABORATOR"
        }
        """;

    private static readonly InlineComment[] Comments =
    [
        new("src/Retry.cs", 12, "Retries forever. (correctness)"),
        new("src/Cache.cs", 3, "Unbounded. (performance)"),
    ];

    private static RepositorySlug ValidSlug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(DelegatingHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static Task<Result<InlineReviewPosting>> PostAsync(GitHubHttpClient sut) =>
        sut.PostPullRequestReviewAsync(
            ValidBaseUrl, ValidSlug, PullRequestUrl, Commit, "2 findings posted inline.", Comments, ValidToken, CancellationToken.None);

    [Fact]
    public async Task WhenPosted_SendsOneCommentReviewWithEveryCommentOnTheRightSide()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, CreatedJson);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        await PostAsync(sut);

        // Assert
        handler.ShouldSatisfyAllConditions(
            () => handler.AllRequests.Count.ShouldBe(1),
            () => handler.LastRequest!.Method.ShouldBe(HttpMethod.Post),
            () => handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/pulls/17/reviews"),
            () => handler.LastRequestBody.ShouldBe(
                """{"commit_id":"ecdd80bb57125d7ba9641ffaa4d7d2c19d3f3091","body":"2 findings posted inline.","event":"COMMENT","comments":[{"path":"src/Retry.cs","line":12,"side":"RIGHT","body":"Retries forever. (correctness)"},{"path":"src/Cache.cs","line":3,"side":"RIGHT","body":"Unbounded. (performance)"}]}"""));
    }

    [Fact]
    public async Task WhenGitHubCreatesTheReview_RegistersEveryCommentByTheReviewsId()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, CreatedJson));

        // Act
        InlineReviewPosting posting = (await PostAsync(sut)).ValueOrThrow();

        // Assert
        posting.ShouldSatisfyAllConditions(
            () => posting.Posted.Select(p => p.Comment).ShouldBe(Comments),
            () => posting.Posted.Select(p => p.Key).Distinct().ShouldHaveSingleItem()
                .ShouldBe(PostedCommentKey.Review(ProviderCommentId.Create(80).ValueOrThrow())),
            () => posting.Failed.ShouldBeEmpty());
    }

    [Fact]
    public async Task WhenGitHubRejectsTheReview_FailsPermanent()
    {
        // Arrange — the shape GitHub's docs give for a validation failure
        FakeHandler handler = new(
            HttpStatusCode.UnprocessableEntity,
            """{ "message": "Validation Failed", "errors": [], "documentation_url": "https://docs.github.com/rest" }""");
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<InlineReviewPosting> result = await PostAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(CommentPostErrors.PermanentCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task WhenGitHubAnswersARetryableStatus_FailsTransient(HttpStatusCode statusCode)
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(statusCode, "{}"));

        // Act
        Result<InlineReviewPosting> result = await PostAsync(sut);

        // Assert
        CommentPostErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheResponseHasNoReviewId_FailsPermanent()
    {
        // Arrange
        GitHubHttpClient sut = BuildSut(new FakeHandler(HttpStatusCode.OK, "{}"));

        // Act
        Result<InlineReviewPosting> result = await PostAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(CommentPostErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenThereAreNoComments_FailsWithoutAProviderCall()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, CreatedJson);
        GitHubHttpClient sut = BuildSut(handler);

        // Act
        Result<InlineReviewPosting> result = await sut.PostPullRequestReviewAsync(
            ValidBaseUrl, ValidSlug, PullRequestUrl, Commit, "summary", [], ValidToken, CancellationToken.None);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(CommentPostErrors.PermanentCode),
            () => handler.AllRequests.ShouldBeEmpty());
    }
}
