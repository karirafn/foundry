using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.ChecksPendingIssueTests;

public sealed class FixChecks
{
    private static readonly IReadOnlyList<FailingCheck> FailingChecks =
    [
        new FailingCheck("ci/build", "failure", "https://github.com/owner/repo/actions/runs/1"),
        new FailingCheck("ci/lint", "failure", null),
    ];

    [Fact]
    public void WhenFixChecksCalledOnFirstAttempt_ReturnsRevisionQueuedIssueWithSameId()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .ChecksPending();

        // Act
        RevisionQueuedIssue revisionQueued = checksPending.FixChecks(FailingChecks);

        // Assert
        revisionQueued.Id.ShouldBe(checksPending.Id);
    }

    [Fact]
    public void WhenFixChecksCalledOnFirstAttempt_RaisesIssueRevisionQueuedDomainEvent()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .ChecksPending();

        // Act
        checksPending.FixChecks(FailingChecks);

        // Assert
        IssueRevisionQueued domainEvent = checksPending.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueRevisionQueued>();
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.IssueId.ShouldBe(checksPending.Id),
            () => domainEvent.MonitoredRepositoryId.ShouldBe(repositoryId));
    }

    [Fact]
    public void WhenFixChecksCalledOnFirstAttempt_RevisionQueuedHasFailedChecksTrigger()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .ChecksPending();

        // Act
        RevisionQueuedIssue revisionQueued = checksPending.FixChecks(FailingChecks);

        // Assert
        RevisionTrigger.FailedChecks trigger = revisionQueued.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>();
        trigger.ShouldSatisfyAllConditions(
            () => trigger.FailingChecks.Count.ShouldBe(2),
            () => trigger.FailingChecks[0].Name.ShouldBe("ci/build"),
            () => trigger.FailingChecks[1].Name.ShouldBe("ci/lint"));
    }

    [Fact]
    public void WhenFixChecksCalledOnFirstAttempt_ChecksFixAttemptsIncrementedToOne()
    {
        // Arrange
        ChecksPendingIssue checksPending = new IssueBuilder()
            .ChecksPending();

        // Act
        RevisionQueuedIssue revisionQueued = checksPending.FixChecks(FailingChecks);

        // Assert
        revisionQueued.ChecksFixAttempts.ShouldBe(1);
    }

    [Fact]
    public void WhenFixChecksCalledOnSecondAttempt_ChecksFixAttemptsIncrementedToTwo()
    {
        // Arrange — simulate a second fix attempt via ChecksPendingFromRevision
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithFailingChecks(FailingChecks)
            .ChecksPendingFromFixChecks();

        // Act
        RevisionQueuedIssue revisionQueued = checksPending.FixChecks(FailingChecks);

        // Assert
        revisionQueued.ChecksFixAttempts.ShouldBe(2);
    }

    [Fact]
    public void WhenFixChecksCalledOnFirstAttempt_BranchAndPullRequestUrlAreCarriedForward()
    {
        // Arrange
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        ChecksPendingIssue checksPending = new IssueBuilder()
            .WithMonitoredRepositoryId(repositoryId)
            .WithBranchName("feat/42-fix-bug")
            .WithPullRequestUrl("https://github.com/owner/repo/pull/10")
            .ChecksPending();

        // Act
        RevisionQueuedIssue revisionQueued = checksPending.FixChecks(FailingChecks);

        // Assert
        revisionQueued.ShouldSatisfyAllConditions(
            () => revisionQueued.BranchName.ShouldBe(checksPending.BranchName),
            () => revisionQueued.PullRequestUrl.ShouldBe(checksPending.PullRequestUrl),
            () => revisionQueued.MonitoredRepositoryId.ShouldBe(repositoryId));
    }
}
