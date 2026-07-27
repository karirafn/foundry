using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;
using Foundry.Modules.Workers.Domain.Entities;
using Foundry.Modules.Workers.Domain.Entities.States;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.Entities.States.FailedRunTests;

public sealed class FromStarting
{
    [Fact]
    public void WhenCreatedFromStarting_ContainerOutputIsAlwaysNull()
    {
        // Arrange
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        FailureReason reason = new FailureReason.ContainerError("image not found");

        // Act
        FailedRun failed = starting.Fail(reason);

        // Assert
        failed.ContainerOutput.ShouldBeNull();
    }
}
