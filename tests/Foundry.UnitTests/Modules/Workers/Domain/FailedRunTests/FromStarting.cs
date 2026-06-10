using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.FailedRunTests;

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
