using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.IssueTests;

public sealed class DemandEscalation
{
    private static readonly DateTimeOffset Now = new(2026, 11, 26, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenAnIssueIsDetected_ItIsNotEscalated()
    {
        // Act
        DetectedIssue detected = new IssueBuilder().Detected();

        // Assert
        detected.DemandEscalated.ShouldBeFalse();
    }

    [Fact]
    public void WhenReviewFindingsQueueARevision_TheDemandEscalates()
    {
        // Act
        RevisionQueuedIssue revision = new IssueBuilder().RevisionQueuedFromReviewFindings();

        // Assert
        revision.ShouldSatisfyAllConditions(
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFindings>(),
            () => revision.DemandEscalated.ShouldBeTrue());
    }

    [Fact]
    public void WhenFailedChecksQueueARevision_TheDemandEscalates()
    {
        // Arrange
        IssueBuilder builder = new();

        // Act
        RevisionQueuedIssue revision = builder.ChecksPending().FixChecks(
            [new FailingCheck("build", "failure", "https://github.com/o/r/actions/runs/1")],
            Now);

        // Assert
        revision.ShouldSatisfyAllConditions(
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.FailedChecks>(),
            () => revision.DemandEscalated.ShouldBeTrue());
    }

    [Fact]
    public void WhenAPersonsReviewFeedbackQueuesARevision_TheDemandIsUnchanged()
    {
        // Act
        RevisionQueuedIssue revision = new IssueBuilder().RevisionQueued();

        // Assert
        revision.ShouldSatisfyAllConditions(
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.ReviewFeedback>(),
            () => revision.DemandEscalated.ShouldBeFalse());
    }

    [Fact]
    public void WhenARebaseConflictQueuesARevision_TheDemandIsUnchanged()
    {
        // Act
        RevisionQueuedIssue revision = new IssueBuilder().RebasePending().ResolveConflict(Now);

        // Assert
        revision.ShouldSatisfyAllConditions(
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.RebaseConflict>(),
            () => revision.DemandEscalated.ShouldBeFalse());
    }

    [Fact]
    public void WhenAnswersQueueARevision_TheDemandIsUnchanged()
    {
        // Arrange
        AwaitingInputIssue awaiting = new IssueBuilder().AwaitingInputPosted(Now);

        // Act
        RevisionQueuedIssue revision = awaiting
            .ProvideInput(ClarificationInput.FromComments("Use Postgres."), Now.AddHours(1), Now.AddHours(1))
            .ShouldBeOfType<RevisionQueuedIssue>();

        // Assert
        revision.ShouldSatisfyAllConditions(
            () => revision.Trigger.ShouldBeOfType<RevisionTrigger.InputProvided>(),
            () => revision.DemandEscalated.ShouldBeFalse());
    }

    [Fact]
    public void WhenAContinuationIsQueued_TheDemandIsUnchanged()
    {
        // Act
        ContinuationQueuedIssue continuation = new IssueBuilder().ContinuationQueued();

        // Assert
        continuation.DemandEscalated.ShouldBeFalse();
    }

    [Fact]
    public void WhenAFreshRunIsQueued_TheDemandIsUnchanged()
    {
        // Act
        FreshQueuedIssue fresh = new IssueBuilder().FreshQueuedFromTriage(IssueKind.Feature);

        // Assert
        fresh.DemandEscalated.ShouldBeFalse();
    }

    [Fact]
    public void WhenAnEscalatingRevisionIsBlocked_TheBlockedRevisionIsEscalated()
    {
        // Act
        RevisionBlockedIssue blocked = new IssueBuilder().RevisionQueuedFromReviewFindings().Block([3], Now);

        // Assert
        blocked.DemandEscalated.ShouldBeTrue();
    }

    [Fact]
    public void WhenAnEscalatedBlockedRevisionUnblocks_TheQueuedRevisionKeepsTheEscalation()
    {
        // Act
        RevisionQueuedIssue queued = new IssueBuilder().RevisionQueuedFromReviewFindings().Block([3], Now).Unblock(Now);

        // Assert
        queued.DemandEscalated.ShouldBeTrue();
    }
}
