using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Monitoring.Domain.Entities;
using Foundry.Modules.Monitoring.Features.Polling;
using Foundry.Modules.Monitoring.Features.Webhooks.Nudges;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Features.Webhooks.Nudges.NudgeSchedulerTests;

public sealed class PendingCategories
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly MonitoredRepositoryId RepoId = MonitoredRepositoryId.New();

    [Theory]
    [InlineData(NudgePass.IssueListing, PollChanges.IssueListing)]
    [InlineData(NudgePass.IssueListingAndDependencies, PollChanges.IssueListing)]
    [InlineData(NudgePass.Review, PollChanges.Review)]
    [InlineData(NudgePass.Checks, PollChanges.Checks)]
    [InlineData(NudgePass.BranchRules, PollChanges.BranchRules)]
    [InlineData(NudgePass.Dependencies, PollChanges.None)]
    [InlineData(NudgePass.Rebase, PollChanges.None)]
    [InlineData(NudgePass.Merge, PollChanges.None)]
    [InlineData(NudgePass.AwaitingInput, PollChanges.None)]
    internal void MapsAPendingPassToItsCategory(NudgePass pass, PollChanges expected)
    {
        // Arrange
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(new NudgeKey(RepoId, pass, 7)), T0);

        // Act
        PollChanges covered = scheduler.PendingCategories(RepoId);

        // Assert
        covered.ShouldBe(expected);
    }

    [Fact]
    public void CoversADeferredNudgeThatIsNotYetDue()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(new NudgeKey(RepoId, NudgePass.Review, 7), T0.AddMinutes(5)), T0);

        // Act
        PollChanges covered = scheduler.PendingCategories(RepoId);

        // Assert
        covered.ShouldBe(PollChanges.Review);
    }

    [Fact]
    public void UnionsCategoriesOfOneRepositoryAndIgnoresAnother()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(new NudgeKey(RepoId, NudgePass.Review, 7)), T0);
        scheduler.Add(new Nudge(new NudgeKey(RepoId, NudgePass.Checks, 7), T0.AddMinutes(5)), T0);
        scheduler.Add(new Nudge(new NudgeKey(MonitoredRepositoryId.New(), NudgePass.BranchRules)), T0);

        // Act
        PollChanges covered = scheduler.PendingCategories(RepoId);

        // Assert
        covered.ShouldBe(PollChanges.Review | PollChanges.Checks);
    }

    [Fact]
    public void WhenTheNudgeHasRun_CoversNothing()
    {
        // Arrange
        NudgeScheduler scheduler = new();
        scheduler.Add(new Nudge(new NudgeKey(RepoId, NudgePass.Review, 7)), T0);
        scheduler.TakeDue(T0.AddSeconds(2), id => id == RepoId);

        // Act
        PollChanges covered = scheduler.PendingCategories(RepoId);

        // Assert
        covered.ShouldBe(PollChanges.None);
    }
}
