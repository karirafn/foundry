using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;
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
        WorkerRunId workerRunId = WorkerRunId.New();
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
        WorkerRunId workerRunId = WorkerRunId.New();
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
        WorkerRunId workerRunId = WorkerRunId.New();
        Guid issueId = Guid.NewGuid();

        // Act
        WorkerRunFailed @event = new(workerRunId, issueId, "reason", Category: "non_zero_exit");

        // Assert
        @event.BranchName.ShouldBeNull();
    }

    [Fact]
    public void WhenCreatedWithCategory_SetsCategory()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        Guid issueId = Guid.NewGuid();

        // Act
        WorkerRunFailed @event = new(workerRunId, issueId, "Non-zero exit code: 1", Category: "non_zero_exit");

        // Assert
        @event.Category.ShouldBe("non_zero_exit");
    }

    [Fact]
    public void WhenCreatedWithoutCategory_CategoryIsNull()
    {
        // Arrange
        WorkerRunId workerRunId = WorkerRunId.New();
        Guid issueId = Guid.NewGuid();

        // Act
        WorkerRunFailed @event = new(workerRunId, issueId, "reason");

        // Assert
        @event.Category.ShouldBeNull();
    }

    [Fact]
    public void CreditsExhaustedReason_MatchesCreditsExhaustedFailureReasonSummary()
    {
        // Arrange
        // Guard: the Issues re-queue handler matches CreditsExhaustedReason with == (exact match).
        // This test ensures the constant stays in lock-step with FailureReason.CreditsExhausted.Summary.
        FailureReason reason = new FailureReason.CreditsExhausted();

        // Act
        string summary = reason.Summary;

        // Assert
        WorkerRunFailed.CreditsExhaustedReason.ShouldBe(summary);
    }
}
