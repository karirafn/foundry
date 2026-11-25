using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Issues.Contracts.Events;
using Foundry.Modules.Issues.Domain.Events;
using Foundry.Modules.Issues.Features.StateChanges;
using Foundry.Modules.Monitoring.Contracts;
using Foundry.Modules.Workers.Contracts;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Features.StateChanges.TriageDecisionsRecordedBridgeTests;

public sealed class HandleAsync
{
    [Fact]
    public async Task WhenTriageDecidedDetails_PublishesOneTriageDecisionsRecordedWithSameFields()
    {
        // Arrange
        CapturingIntegrationEventDispatcher dispatcher = new();
        TriageDecisionsRecordedBridge sut = new(dispatcher);
        IssueId issueId = IssueId.New();
        MonitoredRepositoryId repositoryId = MonitoredRepositoryId.New();
        TriageDecision[] decisions =
        [
            TriageDecisionBuilder.Build(question: "Which cap?"),
            TriageDecisionBuilder.Build(question: "Which log level?", ground: TriageDecisionGround.ImplementationDetail),
        ];
        TriageDetailsDecided domainEvent = new(issueId, repositoryId, 42, decisions, TriageRelease.WaitingOnBlockers);

        // Act
        await sut.HandleAsync(domainEvent, TestContext.Current.CancellationToken);

        // Assert
        TriageDecisionsRecorded published = dispatcher.DispatchedEvents
            .ShouldHaveSingleItem()
            .ShouldBeOfType<TriageDecisionsRecorded>();
        published.ShouldSatisfyAllConditions(
            () => published.IssueId.ShouldBe(issueId.Value),
            () => published.MonitoredRepositoryId.ShouldBe(repositoryId),
            () => published.IssueNumber.ShouldBe(42),
            () => published.Decisions.ShouldBe(decisions),
            () => published.Release.ShouldBe(TriageRelease.WaitingOnBlockers));
    }
}
