using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features.StateChanges;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.StateChanges.IssueTriageQueuedNudgeBridgeTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenIssueTriageQueued_PublishesOneIssueEnteredTriageQueueForItsRepository()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        IssueTriageQueuedNudgeBridge sut = new(dispatcher);
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();

        // Act
        await sut.HandleAsync(new IssueTriageQueued(IssueId.New(), repositoryId), TestContext.Current.CancellationToken);

        // Assert
        IssueEnteredTriageQueue published = dispatcher.DispatchedEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<IssueEnteredTriageQueue>();
        published.RepositoryId.ShouldBe(repositoryId);
    }
}
