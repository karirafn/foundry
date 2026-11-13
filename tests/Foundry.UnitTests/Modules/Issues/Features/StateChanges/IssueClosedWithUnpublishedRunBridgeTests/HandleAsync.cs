using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Features.StateChanges;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

using DomainEvent = Foundry.Modules.Issues.Domain.Events.IssueClosedWithUnpublishedRun;
using IntegrationEvent = Foundry.Modules.Issues.Contracts.Events.IssueClosedWithUnpublishedRun;

namespace Foundry.UnitTests.Modules.Issues.Features.StateChanges.IssueClosedWithUnpublishedRunBridgeTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenIssueClosedWithUnpublishedRun_PublishesOneIntegrationEventForTheSameRun()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        IssueClosedWithUnpublishedRunBridge sut = new(dispatcher);
        WorkerRunId workerRunId = WorkerRunId.New();
        DomainEvent domainEvent = new(IssueId.New(), MonitoredRepositoryId.New(), workerRunId);

        // Act
        await sut.HandleAsync(domainEvent, TestContext.Current.CancellationToken);

        // Assert
        dispatcher.DispatchedEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IntegrationEvent>()
            .WorkerRunId.ShouldBe(workerRunId);
    }
}
