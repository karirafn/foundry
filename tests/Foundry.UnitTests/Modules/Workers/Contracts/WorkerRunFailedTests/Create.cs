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
    public void WhenCreatedWithOptionalFields_SetsThoseFields()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();
        string reasonDescription = "Non-zero exit code: 1";

        // Act
        WorkerRunFailed @event = new(workerRunId, issueId, reasonDescription)
        {
            BranchName = "feat/102-in-progress",
            LatestProgress = "Half done",
        };

        // Assert
        @event.ShouldSatisfyAllConditions(
            () => @event.BranchName.ShouldBe("feat/102-in-progress"),
            () => @event.LatestProgress.ShouldBe("Half done"));
    }

    [Fact]
    public void WhenCreatedWithoutOptionalFields_OptionalFieldsAreNull()
    {
        // Arrange
        Guid workerRunId = Guid.NewGuid();
        Guid issueId = Guid.NewGuid();

        // Act
        WorkerRunFailed @event = new(workerRunId, issueId, "reason");

        // Assert
        @event.ShouldSatisfyAllConditions(
            () => @event.BranchName.ShouldBeNull(),
            () => @event.LatestProgress.ShouldBeNull());
    }
}
