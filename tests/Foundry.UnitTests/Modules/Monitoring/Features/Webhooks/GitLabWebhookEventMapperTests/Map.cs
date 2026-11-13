using System.Text.Json;

using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.GitLabWebhookEventMapperTests;

public sealed class Map
{
    private static readonly MonitoredRepositoryId RepoId = MonitoredRepositoryId.New();
    private static readonly DateTimeOffset At = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    // Issue 10 is tracked and blocked by untracked issue 77; 20 is in review on MR 21;
    // 30 is checks-pending on MR 31; 40 awaits input on its own thread; 50 awaits input on MR 51.
    private static readonly WebhookTrackingContext Tracked = new(
        new HashSet<int> { 10, 20, 30, 40, 50 },
        [new ReviewIssueInfo(20, "https://gitlab.com/group/sub/project/-/merge_requests/21", At, null)],
        [new ChecksPendingIssueInfo(30, "https://gitlab.com/group/sub/project/-/merge_requests/31", At, At)],
        new HashSet<int> { 77 },
        [
            new AwaitingInputThread(40, null),
            new AwaitingInputThread(50, "https://gitlab.com/group/sub/project/-/merge_requests/51"),
        ]);

    private static NudgeKey? MapEvent(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        return GitLabWebhookEventMapper.Map(document.RootElement, RepoId, Tracked);
    }

    [Theory]
    [InlineData("issue")]
    [InlineData("work_item")]
    public void WhenIssueEventForTrackedIssue_NudgesIssueListing(string objectKind)
    {
        MapEvent($$"""{"object_kind":"{{objectKind}}","object_attributes":{"iid":10,"action":"update"},"labels":[]}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListing));
    }

    [Fact]
    public void WhenConfidentialIssueEventForTrackedIssue_NudgesIssueListing()
    {
        MapEvent("""{"object_kind":"issue","event_type":"confidential_issue","object_attributes":{"iid":10,"action":"update","confidential":true}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListing));
    }

    [Fact]
    public void WhenIssueEventForUntrackedIssueWithTriggerLabel_NudgesIssueListing()
    {
        MapEvent("""{"object_kind":"issue","object_attributes":{"iid":99,"action":"open"},"labels":[{"title":"Foundry"}]}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListing));
    }

    [Fact]
    public void WhenTriggerLabelRemovedFromUntrackedIssue_NudgesIssueListing()
    {
        MapEvent("""{"object_kind":"issue","object_attributes":{"iid":99,"action":"update"},"labels":[],"changes":{"labels":{"previous":[{"title":"foundry"}],"current":[]}}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListing));
    }

    [Fact]
    public void WhenIssueEventForUntrackedIssueWithoutTriggerLabel_IsDropped()
    {
        MapEvent("""{"object_kind":"issue","object_attributes":{"iid":99,"action":"open"},"labels":[{"title":"bug"}]}""")
            .ShouldBeNull();
    }

    [Fact]
    public void WhenBlockerOfTrackedIssueCloses_NudgesListingAndDependencies()
    {
        MapEvent("""{"object_kind":"issue","object_attributes":{"iid":77,"action":"close"},"labels":[]}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListingAndDependencies));
    }

    [Fact]
    public void WhenBlockerOfTrackedIssueIsEditedWithoutClosing_IsDropped()
    {
        MapEvent("""{"object_kind":"issue","object_attributes":{"iid":77,"action":"update"},"labels":[]}""")
            .ShouldBeNull();
    }

    [Fact]
    public void WhenMergeRequestEventForReviewIssue_NudgesReviewPassForOwningIssue()
    {
        MapEvent("""{"object_kind":"merge_request","object_attributes":{"iid":21,"source_branch":"other-branch"}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Review, 20));
    }

    [Fact]
    public void WhenMergeRequestEventMatchesWorkerBranch_NudgesReviewPassForOwningIssue()
    {
        MapEvent("""{"object_kind":"merge_request","object_attributes":{"iid":500,"source_branch":"feat/20-thing"}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Review, 20));
    }

    [Fact]
    public void WhenMergeRequestEventForChecksPendingIssue_NudgesChecksPass()
    {
        MapEvent("""{"object_kind":"merge_request","object_attributes":{"iid":31,"source_branch":"feat/30-x"}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Checks, 30));
    }

    [Fact]
    public void WhenMergeRequestEventForUntrackedMergeRequest_IsDropped()
    {
        MapEvent("""{"object_kind":"merge_request","object_attributes":{"iid":500,"source_branch":"main"}}""")
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("note")]
    [InlineData("confidential_note")]
    public void WhenCommentOnReviewMergeRequest_NudgesReviewPass(string eventType)
    {
        MapEvent($$$"""{"object_kind":"note","event_type":"{{{eventType}}}","object_attributes":{"noteable_type":"MergeRequest"},"merge_request":{"iid":21,"source_branch":"x"}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Review, 20));
    }

    [Fact]
    public void WhenCommentOnIssue_IsDropped()
    {
        MapEvent("""{"object_kind":"note","object_attributes":{"noteable_type":"Issue"},"issue":{"iid":10}}""")
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("note")]
    [InlineData("confidential_note")]
    public void WhenCommentOnAwaitingInputIssueWithoutMergeRequest_NudgesAwaitingInputPass(string eventType)
    {
        MapEvent($$$"""{"object_kind":"note","event_type":"{{{eventType}}}","object_attributes":{"noteable_type":"Issue"},"issue":{"iid":40}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.AwaitingInput, 40));
    }

    [Fact]
    public void WhenCommentOnAwaitingInputWorkItem_NudgesAwaitingInputPass()
    {
        MapEvent("""{"object_kind":"note","object_attributes":{"noteable_type":"WorkItem"},"work_item":{"iid":40}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.AwaitingInput, 40));
    }

    [Fact]
    public void WhenCommentOnAwaitingInputIssueWhoseQuestionIsOnItsMergeRequest_IsDropped()
    {
        MapEvent("""{"object_kind":"note","object_attributes":{"noteable_type":"Issue"},"issue":{"iid":50}}""")
            .ShouldBeNull();
    }

    [Fact]
    public void WhenCommentOnAwaitingInputMergeRequest_NudgesAwaitingInputPass()
    {
        MapEvent("""{"object_kind":"note","object_attributes":{"noteable_type":"MergeRequest"},"merge_request":{"iid":51,"source_branch":"feat/50-thing"}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.AwaitingInput, 50));
    }

    [Fact]
    public void WhenMergeRequestEventOnAwaitingInputMergeRequest_IsDropped()
    {
        MapEvent("""{"object_kind":"merge_request","object_attributes":{"iid":51,"source_branch":"feat/50-thing"}}""")
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("success")]
    [InlineData("failed")]
    [InlineData("canceled")]
    [InlineData("manual")]
    public void WhenFinishedPipelineForChecksPendingMergeRequest_NudgesChecksPass(string status)
    {
        MapEvent($$$"""{"object_kind":"pipeline","object_attributes":{"ref":"main","status":"{{{status}}}"},"merge_request":{"iid":31}}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Checks, 30));
    }

    [Fact]
    public void WhenFinishedPipelineOnWorkerBranch_NudgesChecksPass()
    {
        MapEvent("""{"object_kind":"pipeline","object_attributes":{"ref":"feat/30-x","status":"success"},"merge_request":null}""")
            .ShouldBe(new NudgeKey(RepoId, NudgePass.Checks, 30));
    }

    [Theory]
    [InlineData("running")]
    [InlineData("pending")]
    [InlineData("created")]
    public void WhenPipelineStillRunning_IsDropped(string status)
    {
        MapEvent($$$"""{"object_kind":"pipeline","object_attributes":{"ref":"feat/30-x","status":"{{{status}}}"},"merge_request":{"iid":31}}""")
            .ShouldBeNull();
    }

    [Fact]
    public void WhenPipelineForReviewIssue_IsDropped()
    {
        // Pipelines only drive the checks pass; a review issue's pipeline is not a checks-pending owner.
        MapEvent("""{"object_kind":"pipeline","object_attributes":{"ref":"feat/20-thing","status":"success"},"merge_request":{"iid":21}}""")
            .ShouldBeNull();
    }

    [Theory]
    [InlineData("push")]
    [InlineData("tag_push")]
    [InlineData("build")]
    public void WhenUnmappedKind_IsDropped(string objectKind)
    {
        MapEvent($$"""{"object_kind":"{{objectKind}}","ref":"refs/heads/feat/20-thing"}""")
            .ShouldBeNull();
    }
}
