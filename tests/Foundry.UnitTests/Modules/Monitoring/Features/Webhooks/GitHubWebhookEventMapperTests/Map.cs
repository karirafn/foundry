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

    // Issue 10 is tracked (detected) and blocked by untracked issue 77; 20 is in review on PR 21 and
    // blocks tracked issue 30; 30 is checks-pending on PR 31; 40 awaits input on its own thread;
    // 50 awaits input on PR 51.
    private static readonly WebhookTrackingContext Tracked = new(
        new HashSet<int> { 10, 20, 30, 40, 50 },
        [new ReviewIssueInfo(20, "https://github.com/owner/repo/pull/21", At)],
        [new ChecksPendingIssueInfo(30, "https://github.com/owner/repo/pull/31", At, At)],
        new HashSet<int> { 77, 20 },
        [new AwaitingInputThread(40, null), new AwaitingInputThread(50, "https://github.com/owner/repo/pull/51")]);

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
    public void WhenIssueCommentOnAwaitingInputIssueWithoutPullRequest_NudgesAwaitingInputPass()
    {
        MapEvent("issue_comment", """{"action":"created","issue":{"number":40}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.AwaitingInput, 40));
    }

    [Fact]
    public void WhenIssueCommentOnAwaitingInputIssueWhoseQuestionIsOnItsPullRequest_IsDropped()
    {
        MapEvent("issue_comment", """{"action":"created","issue":{"number":50}}""").ShouldBeNull();
    }

    [Fact]
    public void WhenIssueCommentOnAwaitingInputPullRequest_NudgesAwaitingInputPass()
    {
        MapEvent("issue_comment", """{"action":"created","issue":{"number":51,"pull_request":{"url":"x"}}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.AwaitingInput, 50));
    }

    [Theory]
    [InlineData("pull_request_review")]
    [InlineData("pull_request_review_comment")]
    public void WhenReviewOnAwaitingInputPullRequest_NudgesAwaitingInputPass(string eventName)
    {
        MapEvent(eventName, """{"action":"submitted","pull_request":{"number":51,"head":{"ref":"feat/50-thing"}}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.AwaitingInput, 50));
    }

    [Fact]
    public void WhenPullRequestEventOnAwaitingInputPullRequest_IsDropped()
    {
        MapEvent("pull_request", """{"action":"edited","pull_request":{"number":51,"head":{"ref":"feat/50-thing"}}}""")
            .ShouldBeNull();
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

    [Fact]
    public void WhenIssuesEventClosesUntrackedUnlabeledBlocker_NudgesListingAndDependencies()
    {
        MapEvent("issues", """{"action":"closed","issue":{"number":77,"labels":[]}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListingAndDependencies));
    }

    [Fact]
    public void WhenIssuesEventClosesTrackedBlocker_NudgesListingAndDependencies()
    {
        MapEvent("issues", """{"action":"closed","issue":{"number":20,"labels":[{"name":"foundry"}]}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListingAndDependencies));
    }

    [Fact]
    public void WhenIssuesEventClosesTrackedIssueThatBlocksNothing_NudgesIssueListing()
    {
        MapEvent("issues", """{"action":"closed","issue":{"number":10,"labels":[]}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListing));
    }

    [Fact]
    public void WhenIssuesEventClosesLabeledUntrackedIssueThatBlocksNothing_NudgesIssueListing()
    {
        MapEvent("issues", """{"action":"closed","issue":{"number":99,"labels":[{"name":"foundry"}]}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListing));
    }

    [Fact]
    public void WhenIssuesEventClosesUnlabeledUntrackedIssueThatBlocksNothing_IsDropped()
    {
        MapEvent("issues", """{"action":"closed","issue":{"number":99,"labels":[]}}""")
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("reopened")]
    [InlineData("edited")]
    [InlineData("labeled")]
    [InlineData("deleted")]
    public void WhenIssuesEventForUntrackedUnlabeledBlockerIsNotAClose_IsDropped(string action)
    {
        MapEvent("issues", $$$"""{"action":"{{{action}}}","issue":{"number":77,"labels":[]}}""")
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("reopened")]
    [InlineData("edited")]
    public void WhenIssuesEventForTrackedBlockerIsNotAClose_NudgesIssueListing(string action)
    {
        MapEvent("issues", $$$"""{"action":"{{{action}}}","issue":{"number":20,"labels":[]}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListing));
    }

    [Fact]
    public void WhenIssuesEventClosesBlockerButBlockersWereNotLoaded_IsDropped()
    {
        WebhookTrackingContext withoutBlockers = Tracked with { BlockerIssueNumbers = null };
        using JsonDocument document = JsonDocument.Parse("""{"action":"closed","issue":{"number":77,"labels":[]}}""");

        GitHubWebhookEventMapper.Map("issues", document.RootElement, RepoId, withoutBlockers).ShouldBeNull();
    }

    [Theory]
    [InlineData("issues", true)]
    [InlineData("issue_dependencies", false)]
    [InlineData("pull_request", false)]
    [InlineData("pull_request_review", false)]
    [InlineData("pull_request_review_comment", false)]
    [InlineData("issue_comment", false)]
    [InlineData("check_suite", false)]
    [InlineData("status", false)]
    [InlineData("branch_protection_rule", false)]
    [InlineData("repository_ruleset", false)]
    [InlineData("push", false)]
    [InlineData("ping", false)]
    public void NeedsBlockers_IsTrueOnlyForIssuesEvents(string eventName, bool expected)
    {
        GitHubWebhookEventMapper.NeedsBlockers(eventName).ShouldBe(expected);
    }
}
