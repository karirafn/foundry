using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Domain.Events;
using Foundry.Modules.Workers.Features;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.WorkerActivityObservedHandlerTests;

public sealed class HandleAsync
{
    private sealed class CapturingWorkerActivityBroadcaster : IWorkerActivityBroadcaster
    {
        private readonly List<WorkerActivity> _broadcasts = [];

        public IReadOnlyList<WorkerActivity> Broadcasts => _broadcasts;

        public Task BroadcastActivityAsync(WorkerActivity activity, CancellationToken cancellationToken)
        {
            _broadcasts.Add(activity);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task WhenHandled_BroadcastsExactlyOnce()
    {
        // Arrange
        CapturingWorkerActivityBroadcaster broadcaster = new();
        WorkerActivityObservedHandler sut = new(broadcaster);
        WorkerActivityObserved @event = new(
            WorkerRunId.New(),
            IssueId.New(),
            DateTimeOffset.UtcNow,
            NewCommitMarker: null);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        broadcaster.Broadcasts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenHandled_BroadcastsWorkerRunId()
    {
        // Arrange
        CapturingWorkerActivityBroadcaster broadcaster = new();
        WorkerActivityObservedHandler sut = new(broadcaster);
        WorkerRunId workerRunId = WorkerRunId.New();
        WorkerActivityObserved @event = new(
            workerRunId,
            IssueId.New(),
            DateTimeOffset.UtcNow,
            NewCommitMarker: null);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        WorkerActivity activity = broadcaster.Broadcasts.ShouldHaveSingleItem();
        activity.WorkerRunId.ShouldBe(workerRunId.Value);
    }

    [Fact]
    public async Task WhenHandled_BroadcastsIssueId()
    {
        // Arrange
        CapturingWorkerActivityBroadcaster broadcaster = new();
        WorkerActivityObservedHandler sut = new(broadcaster);
        IssueId issueId = IssueId.New();
        WorkerActivityObserved @event = new(
            WorkerRunId.New(),
            issueId,
            DateTimeOffset.UtcNow,
            NewCommitMarker: null);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        WorkerActivity activity = broadcaster.Broadcasts.ShouldHaveSingleItem();
        activity.IssueId.ShouldBe(issueId.Value);
    }

    [Fact]
    public async Task WhenHandled_BroadcastsLastActivityAt()
    {
        // Arrange
        CapturingWorkerActivityBroadcaster broadcaster = new();
        WorkerActivityObservedHandler sut = new(broadcaster);
        DateTimeOffset lastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        WorkerActivityObserved @event = new(
            WorkerRunId.New(),
            IssueId.New(),
            lastActivityAt,
            NewCommitMarker: null);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        WorkerActivity activity = broadcaster.Broadcasts.ShouldHaveSingleItem();
        activity.LastActivityAt.ShouldBe(lastActivityAt);
    }

    [Fact]
    public async Task WhenHandledWithCommitMarker_BroadcastsCommitShaAndMessage()
    {
        // Arrange
        CapturingWorkerActivityBroadcaster broadcaster = new();
        WorkerActivityObservedHandler sut = new(broadcaster);
        CommitMarker marker = CommitMarker.Create(DateTimeOffset.UtcNow, "abc123", "feat: add thing");
        WorkerActivityObserved @event = new(
            WorkerRunId.New(),
            IssueId.New(),
            DateTimeOffset.UtcNow,
            NewCommitMarker: marker);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        WorkerActivity activity = broadcaster.Broadcasts.ShouldHaveSingleItem();
        activity.ShouldSatisfyAllConditions(
            () => activity.NewCommitSha.ShouldBe("abc123"),
            () => activity.NewCommitMessage.ShouldBe("feat: add thing"));
    }

    [Fact]
    public async Task WhenHandledWithNoCommitMarker_BroadcastsNullCommitFields()
    {
        // Arrange
        CapturingWorkerActivityBroadcaster broadcaster = new();
        WorkerActivityObservedHandler sut = new(broadcaster);
        WorkerActivityObserved @event = new(
            WorkerRunId.New(),
            IssueId.New(),
            DateTimeOffset.UtcNow,
            NewCommitMarker: null);

        // Act
        await sut.HandleAsync(@event, CancellationToken.None);

        // Assert
        WorkerActivity activity = broadcaster.Broadcasts.ShouldHaveSingleItem();
        activity.ShouldSatisfyAllConditions(
            () => activity.NewCommitSha.ShouldBeNull(),
            () => activity.NewCommitMessage.ShouldBeNull());
    }
}
