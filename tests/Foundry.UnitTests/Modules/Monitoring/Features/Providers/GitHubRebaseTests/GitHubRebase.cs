using System.Net;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Rebase;
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

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.GitHubRebaseTests;

/// <summary>
/// The GitHub side of the post-run rebase (ADR 0087): reading the head and <c>behind_by</c>, calling
/// <c>updatePullRequestBranch</c>, and classifying GitHub's string-typed error contract (probed 2026-11-04).
/// </summary>
public sealed class GitHubRebase
{
    private static readonly Uri BaseUrl = new("https://api.github.com");
    private const string PrUrl = "https://github.com/owner/repo/pull/42";
    private const string HeadSha = "1111111111111111111111111111111111111111";

    private const string PrStateJson = """
        {
          "data": {
            "rateLimit": { "cost": 1, "remaining": 4999 },
            "repository": { "pullRequest": { "id": "PR_kwDOabc", "headRefOid": "1111111111111111111111111111111111111111", "baseRefName": "main" } }
          }
        }
        """;

    private static RepositorySlug Slug => RepositorySlug.Create("owner/repo").ValueOrThrow();

    private static GitHubHttpClient BuildSut(SequenceHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitHubHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static string ErrorJson(string type, string message) =>
        $$"""{ "data": { "updatePullRequestBranch": null }, "errors": [ { "type": "{{type}}", "message": "{{message}}" } ] }""";

    [Fact]
    public async Task GetRebaseState_WhenBehind_ReturnsBehindWithNodeIdAndHead()
    {
        // Arrange
        SequenceHandler handler = new(
            (HttpStatusCode.OK, PrStateJson),
            (HttpStatusCode.OK, """{ "behind_by": 3, "ahead_by": 2 }"""));

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, PrUrl, "ghp_token", TestContext.Current.CancellationToken);

        // Assert
        PullRequestRebaseState.Behind behind = result.ValueOrThrow().ShouldBeOfType<PullRequestRebaseState.Behind>();
        behind.ShouldSatisfyAllConditions(
            () => behind.PullRequestId.ShouldBe("PR_kwDOabc"),
            () => behind.HeadSha.ShouldBe(HeadSha),
            () => behind.BehindBy.ShouldBe(3));
        handler.Requests[1].ShouldEndWith($"/repos/owner/repo/compare/main...{HeadSha}?per_page=1");
    }

    [Fact]
    public async Task GetRebaseState_WhenBehindByZero_ReturnsUpToDate()
    {
        // Arrange
        SequenceHandler handler = new(
            (HttpStatusCode.OK, PrStateJson),
            (HttpStatusCode.OK, """{ "behind_by": 0, "ahead_by": 2 }"""));

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, PrUrl, "ghp_token", TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBe(new PullRequestRebaseState.UpToDate(HeadSha));
    }

    [Fact]
    public async Task GetRebaseState_WhenCompareIsServerError_FailsTransiently()
    {
        // Arrange
        SequenceHandler handler = new(
            (HttpStatusCode.OK, PrStateJson),
            (HttpStatusCode.BadGateway, "{}"));

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, PrUrl, "ghp_token", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<PullRequestRebaseState>.Failure>();
    }

    [Fact]
    public async Task GetRebaseState_WhenUnauthorized_RefusesWithPermissionDenied()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.Unauthorized, "{}"));

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, PrUrl, "ghp_token", TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBe(new PullRequestRebaseState.Refused(RebaseSkipReasons.PermissionDenied));
    }

    [Fact]
    public async Task RequestRebase_WhenAccepted_ReturnsRequestedAndSendsRebaseMutation()
    {
        // Arrange
        SequenceHandler handler = new(
            (HttpStatusCode.OK, """{ "data": { "updatePullRequestBranch": { "pullRequest": { "headRefOid": "1111111111111111111111111111111111111111" } } } }"""));

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, "PR_kwDOabc", HeadSha, "ghp_token", TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<RebaseRequestOutcome.Requested>();
        string body = handler.Bodies.ShouldHaveSingleItem();
        body.ShouldContain("updatePullRequestBranch");
        body.ShouldContain("updateMethod: REBASE");
        body.ShouldContain("\"expectedHeadOid\":\"" + HeadSha + "\"");
        body.ShouldContain("\"pullRequestId\":\"PR_kwDOabc\"");
    }

    [Theory]
    [InlineData("PullRequest::RebaseConflictError", typeof(RebaseRequestOutcome.Conflict))]
    [InlineData("There are no new commits on the base branch.", typeof(RebaseRequestOutcome.UpToDate))]
    [InlineData("head sha didn't match the current head ref.", typeof(RebaseRequestOutcome.StaleHead))]
    public async Task RequestRebase_ClassifiesUnprocessableByMessage(string message, Type expected)
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.OK, ErrorJson("UNPROCESSABLE", message)));

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, "PR_kwDOabc", HeadSha, "ghp_token", TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().GetType().ShouldBe(expected);
    }

    [Fact]
    public async Task RequestRebase_WhenMessageIsUnrecognised_RefusesUnrecognised()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.OK, ErrorJson("UNPROCESSABLE", "Something new happened.")));

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, "PR_kwDOabc", HeadSha, "ghp_token", TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBe(new RebaseRequestOutcome.Refused(RebaseSkipReasons.UnrecognisedError));
    }

    [Fact]
    public async Task RequestRebase_WhenForbidden_RefusesWithPermissionDenied()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.OK, ErrorJson("FORBIDDEN", "Resource not accessible by integration")));

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, "PR_kwDOabc", HeadSha, "ghp_token", TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBe(new RebaseRequestOutcome.Refused(RebaseSkipReasons.PermissionDenied));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RequestRebase_WhenTransientStatus_Fails(HttpStatusCode status)
    {
        // Arrange
        SequenceHandler handler = new((status, "{}"));

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, "PR_kwDOabc", HeadSha, "ghp_token", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<RebaseRequestOutcome>.Failure>();
    }

    [Fact]
    public async Task RequestRebase_WhenRateLimitedError_Fails()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.OK, ErrorJson("RATE_LIMITED", "API rate limit exceeded")));

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, "PR_kwDOabc", HeadSha, "ghp_token", TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<RebaseRequestOutcome>.Failure>();
    }

    private sealed class SequenceHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _index;

        public List<string> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            (HttpStatusCode status, string body) = responses[_index++];
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
