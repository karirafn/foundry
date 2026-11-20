using System.Text.RegularExpressions;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks.Delivery;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.Delivery.WebhookRelevanceTests;

public sealed class MapChecksDelivery
{
    private static readonly MonitoredRepositoryId RepoId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset At = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly Regex PullPattern = new(@"/pull/(?<number>\d+)$", RegexOptions.CultureInvariant);

    // 20 is in review with passed checks on PR 21; 30 is checks-pending on PR 31; 60 is in review with
    // checks that are not passed on PR 61.
    private static readonly WebhookTrackingContext Tracked = new(
        new HashSet<int> { 20, 30, 60 },
        [
            new ReviewIssueInfo(20, "https://github.com/owner/repo/pull/21", At, null, ChecksPassed: true),
            new ReviewIssueInfo(60, "https://github.com/owner/repo/pull/61", At, null, ChecksPassed: false),
        ],
        [new ChecksPendingIssueInfo(30, "https://github.com/owner/repo/pull/31", At, At)]);

    private static NudgeKey? Map(IReadOnlyList<int> pullNumbers, IReadOnlyList<string> branches) =>
        WebhookRelevance.MapChecksDelivery(RepoId, Tracked, pullNumbers, branches, PullPattern);

    [Fact]
    public void WhenPullRequestOwnedByChecksPendingIssue_NudgesChecksPass()
    {
        Map([31], []).ShouldBe(new NudgeKey(RepoId, NudgePass.Checks, 30));
    }

    [Fact]
    public void WhenBranchOwnedByChecksPendingIssue_NudgesChecksPass()
    {
        Map([], ["feat/30-thing"]).ShouldBe(new NudgeKey(RepoId, NudgePass.Checks, 30));
    }

    [Fact]
    public void WhenPullRequestOwnedByReviewIssueWithChecksNotPassed_NudgesReviewPass()
    {
        Map([61], []).ShouldBe(new NudgeKey(RepoId, NudgePass.Review, 60));
    }

    [Fact]
    public void WhenBranchOwnedByReviewIssueWithChecksNotPassed_NudgesReviewPass()
    {
        Map([], ["feat/60-thing"]).ShouldBe(new NudgeKey(RepoId, NudgePass.Review, 60));
    }

    [Fact]
    public void WhenOwnedOnlyByReviewIssueWithChecksPassed_ReturnsNull()
    {
        Map([21], ["feat/20-thing"]).ShouldBeNull();
    }

    [Fact]
    public void WhenMatchingBothChecksPendingAndReviewIssue_ChecksPendingWins()
    {
        Map([61], ["feat/30-thing"]).ShouldBe(new NudgeKey(RepoId, NudgePass.Checks, 30));
    }

    [Fact]
    public void WhenNothingOwnsTheDelivery_ReturnsNull()
    {
        Map([99], ["main"]).ShouldBeNull();
    }
}
