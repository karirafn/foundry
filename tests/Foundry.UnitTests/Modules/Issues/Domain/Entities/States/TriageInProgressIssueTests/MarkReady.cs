using Foundry.Modules.Issues.Domain.Entities;
using Foundry.Modules.Issues.Domain.Entities.States;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Domain.ValueObjects;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.Entities.States.TriageInProgressIssueTests;

public sealed class MarkReady
{
    private static readonly DateTimeOffset DecidedAt = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenItHasNoOpenBlockers_QueuesItWithTheVerdictRecorded()
    {
        // Arrange
        TriageInProgressIssue triaging = new IssueBuilder().TriageInProgress();
        TriageVerdict.Ready verdict = new(IssueKind.Refactor, DecidedAt);

        // Act
        Issue next = triaging.MarkReady(verdict, DateTimeOffset.UtcNow);

        // Assert
        FreshQueuedIssue queued = next.ShouldBeOfType<FreshQueuedIssue>();
        queued.ShouldSatisfyAllConditions(
            () => queued.Id.ShouldBe(triaging.Id),
            () => queued.TriageVerdict.ShouldBe(verdict),
            () => triaging.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueQueued>());
    }

    [Fact]
    public void WhenItHasOpenBlockers_BlocksItWithTheVerdictRecorded()
    {
        // Arrange
        TriageQueuedIssue queued = new IssueBuilder().TriageQueued();
        queued.SetBlockedBy([3]);
        TriageInProgressIssue triaging = queued.Claim(Foundry.Modules.Workers.Contracts.WorkerRunId.New(), DateTimeOffset.UtcNow);
        TriageVerdict.Ready verdict = new(IssueKind.Feature, DecidedAt);

        // Act
        Issue next = triaging.MarkReady(verdict, DateTimeOffset.UtcNow);

        // Assert
        BlockedIssue blocked = next.ShouldBeOfType<BlockedIssue>();
        blocked.ShouldSatisfyAllConditions(
            () => blocked.BlockedBy.ShouldBe([3]),
            () => blocked.TriageVerdict.ShouldBe(verdict),
            () => triaging.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<IssueBlocked>());
    }

    [Fact]
    public void WhenUnclassified_TheTriagedKindClassifiesIt()
    {
        // Arrange
        TriageInProgressIssue triaging = new IssueBuilder()
            .WithKindClassification(KindClassification.Unclassified)
            .TriageInProgress();

        // Act
        Issue next = triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Bug, DecidedAt), DateTimeOffset.UtcNow);

        // Assert
        next.KindClassification.ShouldBe(new KindClassification.Triaged(IssueKind.Bug));
    }

    [Fact]
    public void WhenLabelled_TheLabelKeepsDecidingAndTheDifferenceIsRecorded()
    {
        // Arrange
        TriageInProgressIssue triaging = new IssueBuilder()
            .WithKindClassification(new KindClassification.Labelled(IssueKind.Bug))
            .TriageInProgress();

        // Act
        Issue next = triaging.MarkReady(new TriageVerdict.Ready(IssueKind.Refactor, DecidedAt), DateTimeOffset.UtcNow);

        // Assert
        next.ShouldSatisfyAllConditions(
            () => next.KindClassification.ShouldBe(new KindClassification.Labelled(IssueKind.Bug)),
            () => next.ConflictingTriageKind.ShouldBe(IssueKind.Refactor));
    }
}
