using System.Net;

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

public sealed class GetPullRequestStateAsync
{
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/123";

    private const string Head = "6dcb09b5b57875f334f61aebed695e2e4193db5e";

    private static readonly Uri ValidBaseUrl = new("https://api.github.com");

    private static RepositorySlug ValidSlug =>
        RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(FakeHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static string Response(string pullRequestJson) => $$"""
        {
          "data": {
            "repository": {
              "pullRequest": {{pullRequestJson}}
            }
          }
        }
        """;

    private static Task<Result<PullRequestState>> ReadAsync(string body) =>
        BuildSut(new FakeHandler(HttpStatusCode.OK, body)).GetPullRequestStateAsync(
            ValidBaseUrl,
            ValidSlug,
            PullRequestUrl,
            token: "ghp_token",
            CancellationToken.None);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WhenPullRequestIsOpenWithAHead_ReturnsOpenAtThatHeadWithItsDraftFlag(bool isDraft)
    {
        // Arrange — field shapes per GitHub's GraphQL PullRequest.headRefOid (GitObjectID!) and isDraft (Boolean!).
        string body = Response($$"""{ "id": "PR_kwDOABCD", "state": "OPEN", "merged": false, "headRefOid": "{{Head}}", "isDraft": {{(isDraft ? "true" : "false")}} }""");

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        PullRequestState.Open open = result.ShouldBeOfType<Result<PullRequestState>.Success>().Value
            .ShouldBeOfType<PullRequestState.Open>();
        open.ShouldSatisfyAllConditions(
            () => open.Head.Value.ShouldBe(Head),
            () => open.IsDraft.ShouldBe(isDraft));
    }

    [Fact]
    public async Task WhenPullRequestIsMerged_ReturnsMerged()
    {
        // Arrange — pullRequest shape captured from GitHub on 2026-11-27: a merged pull request reads state MERGED, merged true.
        string body = Response($$"""{ "state": "MERGED", "merged": true, "headRefOid": "{{Head}}" }""");

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Success>().Value.ShouldBeOfType<PullRequestState.Merged>();
    }

    [Fact]
    public async Task WhenPullRequestIsClosedWithoutMerging_ReturnsClosed()
    {
        // Arrange
        string body = Response($$"""{ "state": "CLOSED", "merged": false, "headRefOid": "{{Head}}" }""");

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Success>().Value.ShouldBeOfType<PullRequestState.Closed>();
    }

    [Fact]
    public async Task WhenMergedPullRequestHasNoHead_StillReturnsMerged()
    {
        // Arrange
        string body = Response("""{ "state": "MERGED", "merged": true }""");

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Success>().Value.ShouldBeOfType<PullRequestState.Merged>();
    }

    [Fact]
    public async Task WhenClosedPullRequestHasAMalformedHead_StillReturnsClosed()
    {
        // Arrange
        string body = Response("""{ "state": "CLOSED", "merged": false, "headRefOid": "not-a-sha" }""");

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Success>().Value.ShouldBeOfType<PullRequestState.Closed>();
    }

    [Fact]
    public async Task WhenStateIsInADifferentCase_StillMapsIt()
    {
        // Arrange
        string body = Response($$"""{ "state": "open", "merged": false, "headRefOid": "{{Head}}" }""");

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Success>().Value.ShouldBeOfType<PullRequestState.Open>();
    }

    [Fact]
    public async Task WhenStateIsUnrecognised_FailsNamingTheState()
    {
        // Arrange
        string body = Response($$"""{ "state": "DRAFTED", "merged": false, "headRefOid": "{{Head}}" }""");

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        Error error = result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(PullRequestStateErrors.UnrecognisedCode),
            () => error.Message.ShouldContain("'DRAFTED'"));
    }

    [Fact]
    public async Task WhenNoPullRequestIsReturned_FailsAsAnUnrecognisedNullState()
    {
        // Arrange
        string body = """{ "data": { "repository": { "pullRequest": null } } }""";

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        Error error = result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error;
        error.ShouldSatisfyAllConditions(
            () => error.Code.ShouldBe(PullRequestStateErrors.UnrecognisedCode),
            () => error.Message.ShouldContain("'<null>'"));
    }

    [Fact]
    public async Task WhenOpenPullRequestHasNoHead_FailsWithMissingHead()
    {
        // Arrange
        string body = Response("""{ "state": "OPEN", "merged": false }""");

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error.Code
            .ShouldBe(PullRequestStateErrors.MissingHeadCode);
    }

    [Fact]
    public async Task WhenOpenPullRequestHasAMalformedHead_FailsWithMissingHead()
    {
        // Arrange — uppercase hex is not a CommitSha.
        string body = Response($$"""{ "state": "OPEN", "merged": false, "headRefOid": "{{Head.ToUpperInvariant()}}" }""");

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error.Code
            .ShouldBe(PullRequestStateErrors.MissingHeadCode);
    }

    [Fact]
    public async Task WhenGraphQlReturnsRateLimitedError_PassesTheFailureThrough()
    {
        // Arrange
        string body = """
            {
              "data": null,
              "errors": [{ "message": "API rate limit exceeded", "type": "RATE_LIMITED" }]
            }
            """;

        // Act
        Result<PullRequestState> result = await ReadAsync(body);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error.Code.ShouldBe("GitHub.RateLimitExhausted");
    }

    [Fact]
    public async Task WhenStateIsRead_UsesThePullRequestStatusQuery()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, Response($$"""{ "state": "OPEN", "merged": false, "headRefOid": "{{Head}}" }"""));

        // Act
        await BuildSut(handler).GetPullRequestStateAsync(ValidBaseUrl, ValidSlug, PullRequestUrl, "ghp_token", CancellationToken.None);

        // Assert
        string requestBody = handler.LastRequestBody.ShouldNotBeNull();
        requestBody.ShouldSatisfyAllConditions(
            () => requestBody.ShouldContain("query PrStatus"),
            () => requestBody.ShouldContain("id state merged headRefOid isDraft"),
            () => requestBody.ShouldContain("123"));
    }

    [Fact]
    public async Task WhenPullRequestUrlCannotBeParsed_FailsWithInvalidPullRequestUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");

        // Act
        Result<PullRequestState> result = await BuildSut(handler).GetPullRequestStateAsync(
            ValidBaseUrl, ValidSlug, "https://github.com/owner/repo/issues/123", "ghp_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error.Code.ShouldBe("GitHub.InvalidPullRequestUrl");
    }

    [Fact]
    public async Task WhenBaseUrlIsNotHttps_FailsWithInvalidBaseUrl()
    {
        // Arrange
        FakeHandler handler = new(HttpStatusCode.OK, "{}");

        // Act
        Result<PullRequestState> result = await BuildSut(handler).GetPullRequestStateAsync(
            new Uri("ftp://api.github.com"), ValidSlug, PullRequestUrl, "ghp_token", CancellationToken.None);

        // Assert
        result.ShouldBeOfType<Result<PullRequestState>.Failure>().Error.Code.ShouldBe("GitHub.InvalidBaseUrl");
    }

    [Theory]
    [InlineData("OPEN", MergeRequestPresence.Open)]
    [InlineData("MERGED", MergeRequestPresence.Merged)]
    [InlineData("CLOSED", MergeRequestPresence.Closed)]
    [InlineData("merged", MergeRequestPresence.Merged)]
    [InlineData("Closed", MergeRequestPresence.Closed)]
    [InlineData("LOCKED", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ParseGraphQlPullRequestState_MapsEachGraphQlStateIgnoringCase(string? state, MergeRequestPresence? expected)
    {
        // Act
        MergeRequestPresence? presence = GitHubHttpClient.ParseGraphQlPullRequestState(state);

        // Assert
        presence.ShouldBe(expected);
    }
}
