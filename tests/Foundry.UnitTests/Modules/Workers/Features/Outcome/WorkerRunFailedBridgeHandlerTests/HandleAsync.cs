using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;
using Foundry.Modules.Workers.Features.Outcome;
using Foundry.Shared;

using DomainWorkerRunFailed = Foundry.Modules.Workers.Domain.Events.WorkerRunFailed;
using IntegrationWorkerRunFailed = Foundry.Modules.Workers.Contracts.Events.WorkerRunFailed;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Features.Outcome.WorkerRunFailedBridgeHandlerTests;

public sealed class HandleAsync
{
    private sealed class CapturingIntegrationEventDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IIntegrationEvent> _captured = [];

        public IReadOnlyList<IIntegrationEvent> Captured => _captured;

        public Task DispatchAsync(IEnumerable<IIntegrationEvent> events, CancellationToken cancellationToken)
        {
            _captured.AddRange(events);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task WhenDomainEventIsHandled_DispatchesIntegrationEventExactlyOnce()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerRunFailedBridgeHandler sut = new(dispatcher);
        DomainWorkerRunFailed domainEvent = new(
            WorkerRunId.New(),
            IssueId.New(),
            ReasonDescription: "Container error",
            Category: "container_error",
            BranchName: null);

        // Act
        await sut.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        dispatcher.Captured.Count.ShouldBe(1);
    }

    [Fact]
    public async Task WhenDomainEventIsHandled_DispatchedIntegrationEventHasMatchingWorkerRunId()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerRunFailedBridgeHandler sut = new(dispatcher);
        WorkerRunId workerRunId = WorkerRunId.New();
        DomainWorkerRunFailed domainEvent = new(
            workerRunId,
            IssueId.New(),
            ReasonDescription: "Container error",
            Category: "container_error",
            BranchName: null);

        // Act
        await sut.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        IntegrationWorkerRunFailed integrationEvent =
            dispatcher.Captured.ShouldHaveSingleItem()
                .ShouldBeOfType<IntegrationWorkerRunFailed>();
        integrationEvent.WorkerRunId.ShouldBe(workerRunId.Value);
    }

    [Fact]
    public async Task WhenDomainEventIsHandled_DispatchedIntegrationEventHasMatchingIssueId()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerRunFailedBridgeHandler sut = new(dispatcher);
        IssueId issueId = IssueId.New();
        DomainWorkerRunFailed domainEvent = new(
            WorkerRunId.New(),
            issueId,
            ReasonDescription: "Container error",
            Category: "container_error",
            BranchName: null);

        // Act
        await sut.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        IntegrationWorkerRunFailed integrationEvent =
            dispatcher.Captured.ShouldHaveSingleItem()
                .ShouldBeOfType<IntegrationWorkerRunFailed>();
        integrationEvent.IssueId.ShouldBe(issueId.Value);
    }

    [Fact]
    public async Task WhenHandledWithNullBranch_DispatchedEventHasNullBranchName()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerRunFailedBridgeHandler sut = new(dispatcher);
        DomainWorkerRunFailed domainEvent = new(
            WorkerRunId.New(),
            IssueId.New(),
            ReasonDescription: "Container error",
            Category: "container_error",
            BranchName: null);

        // Act
        await sut.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        IntegrationWorkerRunFailed integrationEvent =
            dispatcher.Captured.ShouldHaveSingleItem()
                .ShouldBeOfType<IntegrationWorkerRunFailed>();
        integrationEvent.BranchName.ShouldBeNull();
    }

    [Fact]
    public async Task WhenHandledWithNonNullBranch_DispatchedEventHasBranchName()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerRunFailedBridgeHandler sut = new(dispatcher);
        DomainWorkerRunFailed domainEvent = new(
            WorkerRunId.New(),
            IssueId.New(),
            ReasonDescription: "Timed out",
            Category: "timed_out",
            BranchName: "feat/42-partial-work");

        // Act
        await sut.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        IntegrationWorkerRunFailed integrationEvent =
            dispatcher.Captured.ShouldHaveSingleItem()
                .ShouldBeOfType<IntegrationWorkerRunFailed>();
        integrationEvent.BranchName.ShouldBe("feat/42-partial-work");
    }

    [Fact]
    public async Task WhenDomainEventIsHandled_DispatchedIntegrationEventHasMatchingReasonDescription()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerRunFailedBridgeHandler sut = new(dispatcher);
        DomainWorkerRunFailed domainEvent = new(
            WorkerRunId.New(),
            IssueId.New(),
            ReasonDescription: "Branch pre-creation failed: 403 Forbidden",
            Category: "container_error",
            BranchName: null);

        // Act
        await sut.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        IntegrationWorkerRunFailed integrationEvent =
            dispatcher.Captured.ShouldHaveSingleItem()
                .ShouldBeOfType<IntegrationWorkerRunFailed>();
        integrationEvent.ReasonDescription.ShouldBe("Branch pre-creation failed: 403 Forbidden");
    }

    [Fact]
    public async Task WhenDomainEventIsHandled_DispatchedIntegrationEventHasMatchingCategory()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        WorkerRunFailedBridgeHandler sut = new(dispatcher);
        DomainWorkerRunFailed domainEvent = new(
            WorkerRunId.New(),
            IssueId.New(),
            ReasonDescription: "Timed out",
            Category: "timed_out",
            BranchName: null);

        // Act
        await sut.HandleAsync(domainEvent, CancellationToken.None);

        // Assert
        IntegrationWorkerRunFailed integrationEvent =
            dispatcher.Captured.ShouldHaveSingleItem()
                .ShouldBeOfType<IntegrationWorkerRunFailed>();
        integrationEvent.Category.ShouldBe("timed_out");
    }
}
