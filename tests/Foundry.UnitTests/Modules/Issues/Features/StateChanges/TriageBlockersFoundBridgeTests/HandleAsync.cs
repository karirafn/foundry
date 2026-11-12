using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features.StateChanges;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.StateChanges.TriageBlockersFoundBridgeTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenBlockersNamed_PublishesOneTriageBlockersFoundWithSameFields()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        TriageBlockersFoundBridge sut = new(dispatcher);
        IssueId issueId = IssueId.New();
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        WorkerRunId workerRunId = WorkerRunId.New();
        TriageBlocker[] blockers = [new(7, "needs the schema"), new(9, "needs the api")];
        TriageBlockersNamed domainEvent = new(issueId, repositoryId, 42, workerRunId, blockers);

        // Act
        await sut.HandleAsync(domainEvent, TestContext.Current.CancellationToken);

        // Assert
        TriageBlockersFound published = dispatcher.DispatchedEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<TriageBlockersFound>();
        published.IssueId.ShouldBe(issueId.Value);
        published.MonitoredRepositoryId.ShouldBe(repositoryId);
        published.IssueNumber.ShouldBe(42);
        published.WorkerRunId.ShouldBe(workerRunId);
        published.Blockers.ShouldBe(blockers);
    }
}
