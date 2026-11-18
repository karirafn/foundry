using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.ReviewFactsTests;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.StartingRunTests;

public sealed class RecordSuppliedReviewFacts
{
    private static readonly ReviewFacts Facts = ReviewFactsBuilder.With(
        [new ChangedPath(ChangeStatus.Modified, "src/App.cs")],
        AcceptanceCriteria.Extract("## Acceptance Criteria\n\n- works\n"));

    [Fact]
    public void RecordsTheFactsOnTheRun()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);

        // Act
        starting.RecordSuppliedReviewFacts(Facts);

        // Assert
        starting.SuppliedReviewFacts.ShouldBe(Facts);
    }

    [Fact]
    public void WhenNeverRecorded_TheRunHasNone()
    {
        // Act
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);

        // Assert
        starting.SuppliedReviewFacts.ShouldBeNull();
    }

    [Fact]
    public void LaterStatesCarryTheFacts()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New(), WorkerRole.Review);
        starting.RecordSuppliedReviewFacts(Facts);

        // Act
        CompletedRun completed = starting
            .Activate(ContainerId.From("container-1"), branchName: null, MonitoredRepositoryId.New())
            .Complete(0, branchName: null, pullRequestUrl: null);

        // Assert
        completed.SuppliedReviewFacts.ShouldBe(Facts);
    }
}
