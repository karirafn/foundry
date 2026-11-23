using System.Net;
using System.Text.Json;

using Foundry.Modules.Monitoring.Contracts.Commands;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
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

namespace Foundry.UnitTests.Modules.Monitoring.Infrastructure.GitHubIssueProviderTests;

public sealed class MarkPullRequestReadyAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/12";
    private const string NodeId = "PR_kwDOABCD12";

    // Response shapes per GitHub's GraphQL reference: PullRequest.id (ID!) and isDraft (Boolean!), and the
    // markPullRequestReadyForReview payload's pullRequest.
    private const string DraftStatusJson = $$"""
        { "data": { "repository": { "pullRequest": { "id": "{{NodeId}}", "state": "OPEN", "merged": false, "isDraft": true } } } }
        """;

    private const string ReadyStatusJson = $$"""
        { "data": { "repository": { "pullRequest": { "id": "{{NodeId}}", "state": "OPEN", "merged": false, "isDraft": false } } } }
        """;

    private const string MarkedReadyJson = """
        { "data": { "markPullRequestReadyForReview": { "pullRequest": { "isDraft": false } } } }
        """;

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubIssueProvider BuildSut(HttpMessageHandler handler)
    {
        HttpClient httpClient = new(handler);
        GitHubHttpClient gitHubHttpClient = new(
            httpClient,
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);
        return new GitHubIssueProvider(
            gitHubHttpClient,
            new GitHubJobLogClient(httpClient),
            new ActionableFeedbackPolicy(TimeProvider.System),
            ValidToken,
            ValidBaseUrl);
    }

    private static Task<Result> MarkReadyAsync(GitHubIssueProvider sut, string pullRequestUrl = PullRequestUrl) =>
        sut.MarkPullRequestReadyAsync(ValidSlug, pullRequestUrl, CancellationToken.None);

    [Fact]
    public async Task WhenPullRequestIsDraft_SendsTheMarkReadyMutationWithItsNodeId()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, DraftStatusJson), (HttpStatusCode.OK, MarkedReadyJson)]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        using JsonDocument mutation = JsonDocument.Parse(handler.RequestBodies[1]!);
        result.ShouldSatisfyAllConditions(
            () => result.IsSuccess.ShouldBeTrue(),
            () => handler.Requests.Count.ShouldBe(2),
            () => handler.Requests[1].RequestUri!.AbsoluteUri.ShouldBe("https://api.github.com/graphql"),
            () => mutation.RootElement.GetProperty("query").GetString().ShouldNotBeNull()
                .ShouldContain("markPullRequestReadyForReview(input: { pullRequestId: $pullRequestId })"),
            () => mutation.RootElement.GetProperty("variables").GetProperty("pullRequestId").GetString().ShouldBe(NodeId));
    }

    [Fact]
    public async Task WhenPullRequestIsDraft_ReadsTheNodeIdFromThePullRequestUrlsNumber()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, DraftStatusJson), (HttpStatusCode.OK, MarkedReadyJson)]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        await MarkReadyAsync(sut);

        // Assert
        using JsonDocument read = JsonDocument.Parse(handler.RequestBodies[0]!);
        read.RootElement.GetProperty("variables").GetProperty("number").GetInt32().ShouldBe(12);
    }

    [Fact]
    public async Task WhenPullRequestIsAlreadyReady_SucceedsWithoutTheMutation()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, ReadyStatusJson)]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.IsSuccess.ShouldBeTrue(),
            () => handler.Requests.Count.ShouldBe(1));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task WhenTheMutationAnswersRetryableStatus_FailsTransient(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, DraftStatusJson), (statusCode, "{}")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task WhenTheStatusReadAnswersRetryableStatus_FailsTransientWithoutTheMutation(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(statusCode, "{}")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue(),
            () => handler.Requests.Count.ShouldBe(1));
    }

    [Fact]
    public async Task WhenTheMutationIsRateLimitedWithForbidden_FailsTransient()
    {
        // Arrange
        Dictionary<string, string> rateLimited = new() { ["x-ratelimit-remaining"] = "0" };
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, DraftStatusJson, new Dictionary<string, string>()),
            (HttpStatusCode.Forbidden, """{ "message": "API rate limit exceeded" }""", rateLimited),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenTheMutationReportsARateLimitedGraphQlError_FailsTransient()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, DraftStatusJson),
            (HttpStatusCode.OK, """{ "errors": [{ "type": "RATE_LIMITED", "message": "API rate limit exceeded" }] }"""),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task WhenTheMutationIsRejected_FailsPermanent(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, DraftStatusJson), (statusCode, """{ "message": "nope" }""")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenTheMutationReportsAGraphQlError_FailsPermanent()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, DraftStatusJson),
            (HttpStatusCode.OK, """{ "data": { "markPullRequestReadyForReview": null }, "errors": [{ "type": "FORBIDDEN", "message": "Resource not accessible by integration" }] }"""),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenThePullRequestDoesNotExist_FailsPermanentWithoutTheMutation()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.OK, """{ "data": { "repository": { "pullRequest": null } }, "errors": [{ "type": "NOT_FOUND", "message": "Could not resolve to a PullRequest with the number of 12." }] }"""),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => handler.Requests.Count.ShouldBe(1));
    }

    [Fact]
    public async Task WhenTheRequestTimesOut_FailsTransient()
    {
        // Arrange
        ThrowingHandler handler = new(new TaskCanceledException("timeout"));
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut);

        // Assert
        RunPublishingErrors.IsTransient(result.ErrorOrThrow()).ShouldBeTrue();
    }

    [Fact]
    public async Task WhenThePullRequestUrlHasNoNumber_FailsPermanentWithoutCallingProvider()
    {
        // Arrange
        SequentialFakeHandler handler = new(Array.Empty<(HttpStatusCode, string)>());
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await MarkReadyAsync(sut, "https://github.com/owner/repo/issues");

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => handler.Requests.ShouldBeEmpty());
    }
}
