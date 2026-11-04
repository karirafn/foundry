using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.GitHubWebhookEventMapperTests;

public sealed class Map
{
    private static readonly MonitoredRepositoryId RepoId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset At = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    // Issue 10 is tracked (detected); 20 is in review on PR 21; 30 is checks-pending on PR 31.
    private static readonly WebhookTrackingContext Tracked = new(
        new HashSet<int> { 10, 20, 30 },
        [new ReviewIssueInfo(20, "https://github.com/owner/repo/pull/21", At)],
        [new ChecksPendingIssueInfo(30, "https://github.com/owner/repo/pull/31", At, At)]);

    private static NudgeKey? MapEvent(string eventName, string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return GitHubWebhookEventMapper.Map(eventName, document.RootElement, RepoId, Tracked);
    }

    [Fact]
    public void WhenIssuesEventForTrackedIssue_NudgesIssueListing()
    {
        MapEvent("issues", """{"action":"edited","issue":{"number":10,"labels":[]}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListing));
    }

    [Fact]
    public void WhenIssuesEventForUntrackedIssueWithTriggerLabel_NudgesIssueListing()
    {
        MapEvent("issues", """{"action":"opened","issue":{"number":99,"labels":[{"name":"Foundry"}]}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListing));
    }

    [Fact]
    public void WhenIssuesEventLabelsUntrackedIssueWithTriggerLabel_NudgesIssueListing()
    {
        MapEvent("issues", """{"action":"labeled","label":{"name":"foundry"},"issue":{"number":99,"labels":[]}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListing));
    }

    [Fact]
    public void WhenIssuesEventForUntrackedIssueWithoutTriggerLabel_IsDropped()
    {
        MapEvent("issues", """{"action":"opened","issue":{"number":99,"labels":[{"name":"bug"}]}}""")
            .ShouldBeNull();
    }

    [Fact]
    public void WhenIssueDependenciesEventTouchesTrackedIssue_NudgesListingAndDependencies()
    {
        MapEvent("issue_dependencies", """{"action":"blocked_by_added","blocked_issue":{"number":10},"blocking_issue":{"number":77}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListingAndDependencies));
    }

    [Fact]
    public void WhenIssueDependenciesEventTouchesNothingTracked_IsDropped()
    {
        MapEvent("issue_dependencies", """{"action":"blocked_by_added","blocked_issue":{"number":76},"blocking_issue":{"number":77}}""")
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("pull_request")]
    [InlineData("pull_request_review")]
    [InlineData("pull_request_review_comment")]
    public void WhenPullRequestEventForReviewIssue_NudgesReviewPassForOwningIssue(string eventName)
    {
        MapEvent(eventName, """{"pull_request":{"number":21,"head":{"ref":"feat/20-thing"}}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Review, 20));
    }

    [Fact]
    public void WhenPullRequestEventForChecksPendingIssue_NudgesChecksPass()
    {
        MapEvent("pull_request", """{"pull_request":{"number":31,"head":{"ref":"feat/30-thing"}}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Checks, 30));
    }

    [Fact]
    public void WhenPullRequestHeadBranchBelongsToTrackedIssue_MatchesByBranch()
    {
        MapEvent("pull_request", """{"pull_request":{"number":555,"head":{"ref":"fix/20-other"}}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Review, 20));
    }

    [Fact]
    public void WhenPullRequestBelongsToNoTrackedIssue_IsDropped()
    {
        MapEvent("pull_request", """{"pull_request":{"number":555,"head":{"ref":"feature/random"}}}""")
            .ShouldBeNull();
    }

    [Fact]
    public void WhenIssueCommentOnTrackedPullRequest_NudgesReviewPass()
    {
        MapEvent("issue_comment", """{"issue":{"number":21,"pull_request":{"url":"x"}}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Review, 20));
    }

    [Fact]
    public void WhenIssueCommentOnPlainIssue_IsDropped()
    {
        MapEvent("issue_comment", """{"issue":{"number":10}}""").ShouldBeNull();
    }

    [Fact]
    public void WhenCheckSuiteCompletedForChecksPendingIssue_NudgesChecksPass()
    {
        MapEvent("check_suite", """{"action":"completed","check_suite":{"head_branch":"feat/30-thing","pull_requests":[]}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Checks, 30));
    }

    [Fact]
    public void WhenCheckSuiteNotCompleted_IsDropped()
    {
        MapEvent("check_suite", """{"action":"requested","check_suite":{"head_branch":"feat/30-thing","pull_requests":[{"number":31}]}}""")
            .ShouldBeNull();
    }

    [Fact]
    public void WhenCheckSuiteForUntrackedBranch_IsDropped()
    {
        MapEvent("check_suite", """{"action":"completed","check_suite":{"head_branch":"main","pull_requests":[]}}""")
            .ShouldBeNull();
    }

    [Fact]
    public void WhenStatusOnChecksPendingIssueBranch_NudgesChecksPass()
    {
        MapEvent("status", """{"state":"success","branches":[{"name":"feat/30-thing"}]}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Checks, 30));
    }

    [Theory]
    [InlineData("branch_protection_rule")]
    [InlineData("repository_ruleset")]
    public void WhenRulesEvent_NudgesBranchRulesStep(string eventName)
    {
        MapEvent(eventName, """{"action":"edited"}""").ShouldBe(new NudgeKey(RepoId, NudgePass.BranchRules));
    }

    [Theory]
    [InlineData("ping")]
    [InlineData("push")]
    [InlineData("star")]
    public void WhenEventMapsToNoPass_ReturnsNull(string eventName)
    {
        MapEvent(eventName, """{"zen":"Keep it logically awesome."}""").ShouldBeNull();
    }
}
