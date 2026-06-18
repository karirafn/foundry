using Foundry.Modules.Workers.Contracts;
using Foundry.Shared;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.WorkerRunFailedTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_ImplementsIIntegrationEvent()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        string reasonDescription = "Container exited with code 1";

        // Act
        WorkerRunFailed @event = new(workerRunId, issueId, reasonDescription);

        // Assert
        @event.ShouldBeAssignableTo<IIntegrationEvent>();
        @event.ShouldSatisfyAllConditions(
            () => @event.WorkerRunId.ShouldBe(workerRunId),
            () => @event.IssueId.ShouldBe(issueId),
            () => @event.ReasonDescription.ShouldBe(reasonDescription));
    }

    [Fact]
    public void WhenCreatedWithBranchName_SetsBranchName()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        string reasonDescription = "Non-zero exit code: 1";

        // Act
        WorkerRunFailed @event = new(
            workerRunId,
            issueId,
            reasonDescription,
            BranchName: "feat/102-in-progress");

        // Assert
        @event.BranchName.ShouldBe("feat/102-in-progress");
    }

    [Fact]
    public void WhenCreatedWithoutBranchName_BranchNameIsNull()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        // Act
        WorkerRunFailed @event = new(workerRunId, issueId, "reason");

        // Assert
        @event.BranchName.ShouldBeNull();
    }

    [Fact]
    public void WhenCreatedWithoutIsUsageLimitedRequeue_DefaultsToFalse()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        // Act
        WorkerRunFailed @event = new(workerRunId, issueId, "reason");

        // Assert
        @event.IsUsageLimitedRequeue.ShouldBeFalse();
    }

    [Fact]
    public void WhenCreatedWithIsUsageLimitedRequeue_SetsFlag()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        // Act
        WorkerRunFailed @event = new(
            workerRunId,
            issueId,
            "Usage limit reached",
            IsUsageLimitedRequeue: true);

        // Assert
        @event.IsUsageLimitedRequeue.ShouldBeTrue();
    }
}
