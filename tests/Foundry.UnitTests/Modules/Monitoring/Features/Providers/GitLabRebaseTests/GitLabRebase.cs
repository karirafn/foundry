using System.Net;
using System.Text;

using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers.Feedback;
using Foundry.Modules.Monitoring.Features.Providers.Rebase;
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

namespace Foundry.UnitTests.Modules.Monitoring.Features.Providers.GitLabRebaseTests;

/// <summary>
/// The GitLab side of the post-run rebase (ADR 0087): reading the merge request head, how far it is behind
/// (<c>diverged_commits_count</c>) and the rebase's own progress (<c>rebase_in_progress</c> / <c>merge_error</c>),
/// calling <c>PUT .../merge_requests/:iid/rebase</c>, and classifying its <c>403</c> / <c>409</c> refusals.
/// </summary>
public sealed class GitLabRebase
{
    private static readonly Uri BaseUrl = new("https://gitlab.com/api/v4");
    private const string MrUrl = "https://gitlab.com/group/project/-/merge_requests/7";
    private const string HeadSha = "1111111111111111111111111111111111111111";
    private const string Token = "glpat-token";

    private static RepositorySlug Slug => RepositorySlug.Create("group/project").ValueOrThrow();

    private static GitLabHttpClient BuildSut(SequenceHandler handler) =>
        new(
            new HttpClient(handler),
            NullLogger<GitLabHttpClient>.Instance,
            new DefaultBranchCache(new MemoryCache(Options.Create(new MemoryCacheOptions()))),
            new InMemoryProviderRateBudget(),
            TimeProvider.System);

    private static string MergeRequestJson(
        int divergedCommitsCount = 3,
        bool rebaseInProgress = false,
        string? mergeError = null,
        bool hasConflicts = false,
        string detailedMergeStatus = "mergeable")
    {
        string mergeErrorJson = mergeError is null ? "null" : $"\"{mergeError}\"";
        return $$"""
            {
              "iid": 7,
              "state": "opened",
              "sha": "{{HeadSha}}",
              "diverged_commits_count": {{divergedCommitsCount}},
              "rebase_in_progress": {{(rebaseInProgress ? "true" : "false")}},
              "merge_error": {{mergeErrorJson}},
              "has_conflicts": {{(hasConflicts ? "true" : "false")}},
              "detailed_merge_status": "{{detailedMergeStatus}}"
            }
            """;
    }

    [Fact]
    public async Task GetRebaseState_WhenBehind_ReturnsBehindWithIidAndHeadAndReadsRebaseProgress()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.OK, MergeRequestJson(divergedCommitsCount: 3)));

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, MrUrl, Token, TestContext.Current.CancellationToken);

        // Assert
        PullRequestRebaseState.Behind behind = result.ValueOrThrow().ShouldBeOfType<PullRequestRebaseState.Behind>();
        behind.ShouldSatisfyAllConditions(
            () => behind.PullRequestId.ShouldBe("7"),
            () => behind.HeadSha.ShouldBe(HeadSha),
            () => behind.BehindBy.ShouldBe(3),
            () => behind.Progress.ShouldBeOfType<ProviderRebaseProgress.Completed>());
        handler.Requests.ShouldHaveSingleItem().ShouldBe(
            "https://gitlab.com/api/v4/projects/group%2Fproject/merge_requests/7?include_diverged_commits_count=true&include_rebase_in_progress=true");
        handler.Methods.ShouldHaveSingleItem().ShouldBe(HttpMethod.Get);
    }

    [Fact]
    public async Task GetRebaseState_WhenNotDiverged_ReturnsUpToDate()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.OK, MergeRequestJson(divergedCommitsCount: 0)));

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, MrUrl, Token, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBe(
            new PullRequestRebaseState.UpToDate(HeadSha) { Progress = new ProviderRebaseProgress.Completed() });
    }

    [Fact]
    public async Task GetRebaseState_WhenRebaseInProgress_ReportsInProgress()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.OK, MergeRequestJson(rebaseInProgress: true)));

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, MrUrl, Token, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().Progress.ShouldBeOfType<ProviderRebaseProgress.InProgress>();
    }

    [Fact]
    public async Task GetRebaseState_WhenMergeErrorAndConflicts_ReportsConflicted()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.OK, MergeRequestJson(
            mergeError: "Rebase failed: Rebase locally, resolve all conflicts, then push the branch.",
            hasConflicts: true,
            detailedMergeStatus: "conflict")));

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, MrUrl, Token, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().Progress.ShouldBeOfType<ProviderRebaseProgress.Conflicted>();
    }

    [Fact]
    public async Task GetRebaseState_WhenMergeErrorWithoutConflicts_ReportsFailed()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.OK, MergeRequestJson(
            mergeError: "Rebase failed: Rebase locally, resolve all conflicts, then push the branch.")));

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, MrUrl, Token, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().Progress.ShouldBe(new ProviderRebaseProgress.Failed(RebaseSkipReasons.RebaseFailed));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task GetRebaseState_WhenTransientStatus_Fails(HttpStatusCode status)
    {
        // Arrange
        SequenceHandler handler = new((status, "{}"));

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, MrUrl, Token, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<PullRequestRebaseState>.Failure>();
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, RebaseSkipReasons.PermissionDenied)]
    [InlineData(HttpStatusCode.Forbidden, RebaseSkipReasons.PermissionDenied)]
    [InlineData(HttpStatusCode.NotFound, RebaseSkipReasons.NotFound)]
    public async Task GetRebaseState_WhenRefused_RefusesWithTheReason(HttpStatusCode status, string reason)
    {
        // Arrange
        SequenceHandler handler = new((status, "{}"));

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, MrUrl, Token, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBe(new PullRequestRebaseState.Refused(reason));
    }

    [Fact]
    public async Task GetRebaseState_WhenUrlHasNoIid_RefusesNotFoundWithoutACall()
    {
        // Arrange
        SequenceHandler handler = new();

        // Act
        Result<PullRequestRebaseState> result = await BuildSut(handler)
            .GetRebaseStateAsync(BaseUrl, Slug, "https://gitlab.com/group/project", Token, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBe(new PullRequestRebaseState.Refused(RebaseSkipReasons.NotFound));
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task RequestRebase_WhenAccepted_ReturnsRequestedAndPutsTheRebase()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.Accepted, """{ "rebase_in_progress": true }"""));

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, Slug, "7", Token, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBeOfType<RebaseRequestOutcome.Requested>();
        handler.Requests.ShouldHaveSingleItem().ShouldBe(
            "https://gitlab.com/api/v4/projects/group%2Fproject/merge_requests/7/rebase");
        handler.Methods.ShouldHaveSingleItem().ShouldBe(HttpMethod.Put);
    }

    [Theory]
    [InlineData("403 Forbidden - Source branch is protected from force push", RebaseSkipReasons.ForcePushProtected)]
    [InlineData("403 Forbidden - Cannot push to source branch", RebaseSkipReasons.PermissionDenied)]
    [InlineData("403 Forbidden - Source branch does not exist", RebaseSkipReasons.NotFound)]
    [InlineData("403 Forbidden", RebaseSkipReasons.PermissionDenied)]
    public async Task RequestRebase_WhenForbidden_ClassifiesByMessage(string message, string reason)
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.Forbidden, $$"""{ "message": "{{message}}" }"""));

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, Slug, "7", Token, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBe(new RebaseRequestOutcome.Refused(reason));
    }

    [Fact]
    public async Task RequestRebase_WhenForbiddenWithoutAJsonBody_RefusesWithPermissionDenied()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.Forbidden, "Forbidden"));

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, Slug, "7", Token, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBe(new RebaseRequestOutcome.Refused(RebaseSkipReasons.PermissionDenied));
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    public async Task RequestRebase_WhenTransientStatus_Fails(HttpStatusCode status)
    {
        // Arrange — 409 is GitLab's "Failed to enqueue the rebase operation": a retry may help.
        SequenceHandler handler = new((status, """{ "message": "Failed to enqueue the rebase operation, possibly due to a long-lived transaction. Try again later." }"""));

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, Slug, "7", Token, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<RebaseRequestOutcome>.Failure>();
    }

    [Fact]
    public async Task RequestRebase_WhenRateLimitedForbidden_Fails()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.Forbidden, "{}")) { RateLimitRemaining = "0" };

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, Slug, "7", Token, TestContext.Current.CancellationToken);

        // Assert
        result.ShouldBeOfType<Result<RebaseRequestOutcome>.Failure>();
    }

    [Fact]
    public async Task RequestRebase_WhenUnexpectedStatus_RefusesUnrecognised()
    {
        // Arrange
        SequenceHandler handler = new((HttpStatusCode.UnprocessableEntity, "{}"));

        // Act
        Result<RebaseRequestOutcome> result = await BuildSut(handler)
            .RequestRebaseAsync(BaseUrl, Slug, "7", Token, TestContext.Current.CancellationToken);

        // Assert
        result.ValueOrThrow().ShouldBe(new RebaseRequestOutcome.Refused(RebaseSkipReasons.UnrecognisedError));
    }

    [Fact]
    public async Task Provider_RoutesTheRebaseThroughTheMergeRequestIid()
    {
        // Arrange — the iid read by GetRebaseState is the id RequestRebase sends back.
        SequenceHandler handler = new(
            (HttpStatusCode.OK, MergeRequestJson(divergedCommitsCount: 2)),
            (HttpStatusCode.Accepted, """{ "rebase_in_progress": true }"""));
        GitLabIssueProvider provider = new(
            BuildSut(handler),
            new ActionableFeedbackPolicy(TimeProvider.System),
            Token,
            BaseUrl);

        // Act
        PullRequestRebaseState.Behind behind = (await provider
            .GetRebaseStateAsync(Slug, MrUrl, TestContext.Current.CancellationToken))
            .ValueOrThrow()
            .ShouldBeOfType<PullRequestRebaseState.Behind>();
        Result<RebaseRequestOutcome> outcome = await provider
            .RequestRebaseAsync(Slug, behind.PullRequestId, behind.HeadSha, TestContext.Current.CancellationToken);

        // Assert
        outcome.ValueOrThrow().ShouldBeOfType<RebaseRequestOutcome.Requested>();
        handler.Requests[1].ShouldEndWith("/merge_requests/7/rebase");
    }

    [Theory]
    [InlineData(true, null, false, null, typeof(ProviderRebaseProgress.InProgress))]
    [InlineData(true, "Rebase failed", true, "conflict", typeof(ProviderRebaseProgress.InProgress))]
    [InlineData(false, null, false, "mergeable", typeof(ProviderRebaseProgress.Completed))]
    [InlineData(null, null, null, null, typeof(ProviderRebaseProgress.Completed))]
    [InlineData(false, "Rebase failed", true, "mergeable", typeof(ProviderRebaseProgress.Conflicted))]
    [InlineData(false, "Rebase failed", false, "conflict", typeof(ProviderRebaseProgress.Conflicted))]
    [InlineData(false, "Rebase failed", false, "not_open", typeof(ProviderRebaseProgress.Failed))]
    public void ClassifyRebaseProgress_ClassifiesGitLabsReport(
        bool? rebaseInProgress,
        string? mergeError,
        bool? hasConflicts,
        string? detailedMergeStatus,
        Type expected)
    {
        // Act
        ProviderRebaseProgress progress = GitLabHttpClient.ClassifyRebaseProgress(
            rebaseInProgress,
            mergeError,
            hasConflicts,
            detailedMergeStatus);

        // Assert
        progress.ShouldBeOfType(expected);
    }

    private sealed class SequenceHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _index;

        public List<string> Requests { get; } = [];

        public List<HttpMethod> Methods { get; } = [];

        public string? RateLimitRemaining { get; init; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.ToString());
            Methods.Add(request.Method);

            (HttpStatusCode status, string body) = responses[_index++];
            HttpResponseMessage response = new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            if (RateLimitRemaining is { } remaining)
            {
                response.Headers.Add("RateLimit-Remaining", remaining);
            }

            return Task.FromResult(response);
        }
    }
}
