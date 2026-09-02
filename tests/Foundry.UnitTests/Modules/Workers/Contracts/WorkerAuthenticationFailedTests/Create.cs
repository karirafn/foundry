using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.WorkerAuthenticationFailedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        Guid issueId = Guid.NewGuid();
        string reason = WorkerRunFailed.AuthInvalidReason;

        // Act
        WorkerAuthenticationFailed @event = new(workerRunId, issueId, reason);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.ShouldSatisfyAllConditions(
            () => @event.WorkerRunId.ShouldBe(workerRunId),
            () => @event.IssueId.ShouldBe(issueId),
            () => @event.Reason.ShouldBe(reason));
    }
}
