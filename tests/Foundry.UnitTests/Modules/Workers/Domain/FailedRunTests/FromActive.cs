using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.FailedRunTests;

public sealed class FromActive
{
    private static ActiveRun CreateActiveRun(IssueId? issueId = null)
    {
        StartingRun starting = StartingRun.Begin(issueId ?? IssueId.New(), WorkerRunId.New());
        return starting.Activate(ContainerId.From("container-123"), BranchName.From("feat/1-default"));
    }

    [Fact]
    public void WhenContainerOutputProvided_PreservesContainerOutput()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        FailureReason reason = new FailureReason.NonZeroExit(1);
        string containerOutput = "Error: something went wrong\nStack trace...";

        // Act
        FailedRun failed = active.Fail(reason, containerOutput);

        // Assert
        failed.ContainerOutput.ShouldBe(containerOutput);
    }

    [Fact]
    public void WhenContainerOutputNotProvided_ContainerOutputIsNull()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        FailureReason reason = new FailureReason.NonZeroExit(1);

        // Act
        FailedRun failed = active.Fail(reason);

        // Assert
        failed.ContainerOutput.ShouldBeNull();
    }
}
