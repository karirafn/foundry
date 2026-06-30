using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Events.WorkerActivityObservedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIDomainEvent()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueId issueId = IssueId.New();
        DateTimeOffset lastActivityAt = DateTimeOffset.UtcNow;

        // Act
        WorkerActivityObserved domainEvent = new(workerRunId, issueId, lastActivityAt, null);

        // Assert
        domainEvent.ShouldBeAssignableTo<IDomainEvent>();
    }

    [Fact]
    public void WhenCreatedWithNoCommitMarker_PropertiesAreSet()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueId issueId = IssueId.New();
        DateTimeOffset lastActivityAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);

        // Act
        WorkerActivityObserved domainEvent = new(workerRunId, issueId, lastActivityAt, null);

        // Assert
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(workerRunId),
            () => domainEvent.IssueId.ShouldBe(issueId),
            () => domainEvent.LastActivityAt.ShouldBe(lastActivityAt),
            () => domainEvent.NewCommitMarker.ShouldBeNull());
    }

    [Fact]
    public void WhenCreatedWithCommitMarker_CommitMarkerIsSet()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueId issueId = IssueId.New();
        DateTimeOffset lastActivityAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        CommitMarker marker = CommitMarker.Create(lastActivityAt, "abc1234", "feat: something");

        // Act
        WorkerActivityObserved domainEvent = new(workerRunId, issueId, lastActivityAt, marker);

        // Assert
        domainEvent.NewCommitMarker.ShouldBe(marker);
    }
}
