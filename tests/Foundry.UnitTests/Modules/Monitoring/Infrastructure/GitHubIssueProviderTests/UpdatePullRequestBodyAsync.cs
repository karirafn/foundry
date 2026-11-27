using System.Net;

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

public sealed class UpdatePullRequestBodyAsync
{
    private static readonly Uri ValidBaseUrl = new("https://api.github.com");
    private const string ValidToken = "ghp_token";
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/12";
    private const string UpdatedJson = """{ "number": 12, "body": "Body\n\nCloses #42" }""";

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

    private static Task<Result> UpdateAsync(GitHubIssueProvider sut, string pullRequestUrl = PullRequestUrl) =>
        sut.UpdatePullRequestBodyAsync(ValidSlug, pullRequestUrl, "Body\n\nCloses #42", CancellationToken.None);

    [Fact]
    public async Task WhenAccepted_PatchesOnlyTheBodyOfThePullRequest()
    {
        // Arrange
        SequentialFakeHandler handler = new([(HttpStatusCode.OK, UpdatedJson)]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await UpdateAsync(sut);

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ShouldBeOfType<Result.Success>(),
            () => handler.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Patch),
            () => handler.Requests[0].RequestUri!.AbsolutePath.ShouldBe("/repos/owner/repo/pulls/12"),
            () => handler.Requests[0].Headers.Authorization!.Parameter.ShouldBe(ValidToken),
            () => handler.RequestBodies[0].ShouldBe("""{"body":"Body\n\nCloses #42"}"""));
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task WhenProviderRefusesTheToken_FailsRefused(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(statusCode, """{ "message": "nope" }""")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await UpdateAsync(sut);

        // Assert
        Error error = result.ErrorOrThrow();
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(RunPublishingErrors.RefusedCode),
            () => error.Message.ShouldContain($"returned {(int)statusCode}"));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task WhenProviderAnswersRetryableStatus_FailsTransient(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(statusCode, "{}")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await UpdateAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.TransientCode);
    }

    [Fact]
    public async Task WhenForbiddenByRateLimit_FailsTransient()
    {
        // Arrange
        SequentialFakeHandler handler = new(
        [
            (HttpStatusCode.Forbidden, """{ "message": "API rate limit exceeded" }""",
                new Dictionary<string, string> { ["X-RateLimit-Remaining"] = "0" }),
        ]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await UpdateAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.TransientCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task WhenProviderRejectsTheRequest_FailsPermanent(HttpStatusCode statusCode)
    {
        // Arrange
        SequentialFakeHandler handler = new([(statusCode, """{ "message": "nope" }""")]);
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await UpdateAsync(sut);

        // Assert
        result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode);
    }

    [Fact]
    public async Task WhenTheUrlNamesNoPullRequest_FailsPermanentWithoutCallingProvider()
    {
        // Arrange
        SequentialFakeHandler handler = new(Array.Empty<(HttpStatusCode, string)>());
        GitHubIssueProvider sut = BuildSut(handler);

        // Act
        Result result = await UpdateAsync(sut, "https://github.com/owner/repo/issues/12");

        // Assert
        result.ShouldSatisfyAllConditions(
            () => result.ErrorOrThrow().Code.ShouldBe(RunPublishingErrors.PermanentCode),
            () => handler.Requests.ShouldBeEmpty());
    }
}
