using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.PullRequestWatch;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

using Watch = Foundry.Modules.Monitoring.Features.PullRequestWatch.PullRequestWatch;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PullRequestWatch.PullRequestWatchTests;

/// <summary>Tests for the watch read that starts from an issue status the caller already read.</summary>
public sealed class ReadAsyncFromIssueStatus
{
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/99";

    private static readonly RepositorySlug Slug = RepositorySlug.Create("owner/repo").ValueOrThrow();
    private static readonly CommitSha Head = CommitSha.From("1111111111111111111111111111111111111111");
    private static readonly IssueStatus OpenIssue = new(IsClosed: false, Body: "body");
    private static readonly IssueStatus ClosedIssue = new(IsClosed: true, Body: "body");

    private readonly ScriptedIssueProvider _provider = new();

    [Fact]
    public async Task WhenIssueClosedAndPullRequestMerged_ReturnsIssueClosedWithoutReadingPullRequest()
    {
        _provider.PullRequestStateResult = Result<PullRequestState>.Ok(new PullRequestState.Merged());

        Result<PullRequestWayOut> result = await WatchAsync(ClosedIssue);

        result.ShouldBeOfType<Result<PullRequestWayOut>.Success>().Value.ShouldBeOfType<PullRequestWayOut.IssueClosed>();
        _provider.IssueReads.ShouldBe(0);
        _provider.PullRequestReads.ShouldBe(0);
    }

    [Fact]
    public async Task WhenPullRequestClosedWithoutMerging_ReturnsPullRequestClosed()
    {
        _provider.PullRequestStateResult = Result<PullRequestState>.Ok(new PullRequestState.Closed());

        Result<PullRequestWayOut> result = await WatchAsync(OpenIssue);

        result.ShouldBeOfType<Result<PullRequestWayOut>.Success>().Value.ShouldBeOfType<PullRequestWayOut.PullRequestClosed>();
        _provider.IssueReads.ShouldBe(0);
    }

    [Fact]
    public async Task WhenPullRequestMerged_ReturnsMerged()
    {
        _provider.PullRequestStateResult = Result<PullRequestState>.Ok(new PullRequestState.Merged());

        Result<PullRequestWayOut> result = await WatchAsync(OpenIssue);

        result.ShouldBeOfType<Result<PullRequestWayOut>.Success>().Value.ShouldBeOfType<PullRequestWayOut.Merged>();
    }

    [Fact]
    public async Task WhenPullRequestOpen_ReturnsOpenWithItsHeadAndDraftFlag()
    {
        _provider.PullRequestStateResult = Result<PullRequestState>.Ok(new PullRequestState.Open(Head, IsDraft: true));

        Result<PullRequestWayOut> result = await WatchAsync(OpenIssue);

        PullRequestWayOut.Open open = result.ShouldBeOfType<Result<PullRequestWayOut>.Success>().Value
            .ShouldBeOfType<PullRequestWayOut.Open>();
        open.Head.ShouldBe(Head);
        open.IsDraft.ShouldBeTrue();
    }

    [Fact]
    public async Task WhenPullRequestReadFails_ReturnsItsFailure()
    {
        Error error = PullRequestStateErrors.Unrecognised("WEIRD");
        _provider.PullRequestStateResult = Result<PullRequestState>.Fail(error);

        Result<PullRequestWayOut> result = await WatchAsync(OpenIssue);

        result.ShouldBeOfType<Result<PullRequestWayOut>.Failure>().Error.ShouldBe(error);
    }

    private Task<Result<PullRequestWayOut>> WatchAsync(IssueStatus issue) =>
        Watch.ReadAsync(_provider, Slug, issue, PullRequestUrl, TestContext.Current.CancellationToken);
}
