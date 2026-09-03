using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Modules.Workers.Domain.ValueObjects;
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
        WorkerActivityObserved domainEvent = new(workerRunId, issueId, lastActivityAt, CommitCount: 0);

        // Assert
        domainEvent.ShouldBeAssignableTo<IDomainEvent>();
    }

    [Fact]
    public void WhenCreated_PropertiesAreSet()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        IssueId issueId = IssueId.New();
        DateTimeOffset lastActivityAt = new(2026, 6, 29, 10, 0, 0, TimeSpan.Zero);
        int commitCount = 3;

        // Act
        WorkerActivityObserved domainEvent = new(workerRunId, issueId, lastActivityAt, commitCount);

        // Assert
        domainEvent.ShouldSatisfyAllConditions(
            () => domainEvent.WorkerRunId.ShouldBe(workerRunId),
            () => domainEvent.IssueId.ShouldBe(issueId),
            () => domainEvent.LastActivityAt.ShouldBe(lastActivityAt),
            () => domainEvent.CommitCount.ShouldBe(commitCount));
    }
}
