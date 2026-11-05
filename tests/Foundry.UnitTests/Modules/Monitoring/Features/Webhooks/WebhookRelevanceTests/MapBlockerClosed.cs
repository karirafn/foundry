using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Features.Webhooks;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.WebhookRelevanceTests;

public sealed class MapBlockerClosed
{
    private static readonly MonitoredRepositoryId RepoId = MonitoredRepositoryId.New();

    // Issue 77 blocks a tracked issue; 10 is tracked but blocks nothing.
    private static readonly WebhookTrackingContext Tracked = new(
        new HashSet<int> { 10 },
        [],
        [],
        new HashSet<int> { 77 });

    [Fact]
    public void WhenBlockerCloses_NudgesListingAndDependencies()
    {
        WebhookRelevance.MapBlockerClosed(RepoId, Tracked, 77, isClose: true)
            .ShouldBe(new NudgeKey(RepoId, NudgePass.IssueListingAndDependencies));
    }

    [Fact]
    public void WhenBlockerChangesWithoutClosing_ReturnsNull()
    {
        WebhookRelevance.MapBlockerClosed(RepoId, Tracked, 77, isClose: false).ShouldBeNull();
    }

    [Fact]
    public void WhenClosingIssueBlocksNothing_ReturnsNull()
    {
        WebhookRelevance.MapBlockerClosed(RepoId, Tracked, 10, isClose: true).ShouldBeNull();
    }

    [Fact]
    public void WhenBlockersWereNotLoaded_ReturnsNull()
    {
        WebhookRelevance.MapBlockerClosed(RepoId, Tracked with { BlockerIssueNumbers = null }, 77, isClose: true)
            .ShouldBeNull();
    }
}
