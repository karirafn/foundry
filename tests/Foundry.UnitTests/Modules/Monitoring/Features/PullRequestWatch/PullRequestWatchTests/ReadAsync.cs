using Foundry.Modules.Monitoring.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Features.Providers;
using Foundry.Modules.Monitoring.Features.PullRequestWatch;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

using Watch = Foundry.Modules.Monitoring.Features.PullRequestWatch.PullRequestWatch;

namespace Foundry.UnitTests.Modules.Monitoring.Features.PullRequestWatch.PullRequestWatchTests;

/// <summary>Tests for the watch read that reads the issue's closure itself, then its pull request.</summary>
public sealed class ReadAsync
{
    private const int IssueNumber = 42;
    private const string PullRequestUrl = "https://github.com/owner/repo/pull/99";

    private static readonly RepositorySlug Slug = RepositorySlug.Create("owner/repo").ValueOrThrow();
    private static readonly CommitSha Head = CommitSha.From("1111111111111111111111111111111111111111");

    private readonly ScriptedIssueProvider _provider = new();

    [Fact]
    public async Task WhenIssueClosedAndPullRequestMerged_ReturnsIssueClosedWithoutReadingPullRequest()
    {
        _provider.IssueClosedResult = Result<bool>.Ok(true);
        _provider.PullRequestStateResult = Result<PullRequestState>.Ok(new PullRequestState.Merged());

        Result<PullRequestWayOut> result = await WatchAsync();

        result.ShouldBeOfType<Result<PullRequestWayOut>.Success>().Value.ShouldBeOfType<PullRequestWayOut.IssueClosed>();
        _provider.PullRequestReads.ShouldBe(0);
    }

    [Fact]
    public async Task WhenPullRequestClosedWithoutMerging_ReturnsPullRequestClosed()
    {
        _provider.PullRequestStateResult = Result<PullRequestState>.Ok(new PullRequestState.Closed());

        Result<PullRequestWayOut> result = await WatchAsync();

        result.ShouldBeOfType<Result<PullRequestWayOut>.Success>().Value.ShouldBeOfType<PullRequestWayOut.PullRequestClosed>();
    }

    [Fact]
    public async Task WhenPullRequestMerged_ReturnsMerged()
    {
        _provider.PullRequestStateResult = Result<PullRequestState>.Ok(new PullRequestState.Merged());

        Result<PullRequestWayOut> result = await WatchAsync();

        result.ShouldBeOfType<Result<PullRequestWayOut>.Success>().Value.ShouldBeOfType<PullRequestWayOut.Merged>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WhenPullRequestOpen_ReturnsOpenWithItsHeadAndDraftFlag(bool isDraft)
    {
        _provider.PullRequestStateResult = Result<PullRequestState>.Ok(new PullRequestState.Open(Head, isDraft));

        Result<PullRequestWayOut> result = await WatchAsync();

        PullRequestWayOut.Open open = result.ShouldBeOfType<Result<PullRequestWayOut>.Success>().Value
            .ShouldBeOfType<PullRequestWayOut.Open>();
        open.Head.ShouldBe(Head);
        open.IsDraft.ShouldBe(isDraft);
    }

    [Fact]
    public async Task WhenIssueReadFails_ReturnsItsFailureWithoutReadingPullRequest()
    {
        Error error = new("Issue.ReadFailed", "issue read failed");
        _provider.IssueClosedResult = Result<bool>.Fail(error);

        Result<PullRequestWayOut> result = await WatchAsync();

        result.ShouldBeOfType<Result<PullRequestWayOut>.Failure>().Error.ShouldBe(error);
        _provider.PullRequestReads.ShouldBe(0);
    }

    [Fact]
    public async Task WhenPullRequestReadFails_ReturnsItsFailure()
    {
        Error error = PullRequestStateErrors.MissingHead();
        _provider.PullRequestStateResult = Result<PullRequestState>.Fail(error);

        Result<PullRequestWayOut> result = await WatchAsync();

        result.ShouldBeOfType<Result<PullRequestWayOut>.Failure>().Error.ShouldBe(error);
    }

    private Task<Result<PullRequestWayOut>> WatchAsync() =>
        Watch.ReadAsync(_provider, Slug, IssueNumber, PullRequestUrl, TestContext.Current.CancellationToken);
}
