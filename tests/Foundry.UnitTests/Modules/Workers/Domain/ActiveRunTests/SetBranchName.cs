using Foundry.Modules.Issues.Contracts;
using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ActiveRunTests;

public sealed class SetBranchName
{
    private static ActiveRun CreateActiveRun()
    {
        StartingRun starting = StartingRun.Begin(IssueId.New(), WorkerRunId.New());
        return starting.Activate(ContainerId.From("container-123"));
    }

    [Fact]
    public void WhenBranchNameIsNull_SetsBranchName()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();

        // Act
        active.SetBranchName(BranchName.From("feat/my-feature"));

        // Assert
        active.BranchName.ShouldBe(BranchName.From("feat/my-feature"));
    }

    [Fact]
    public void WhenBranchNameAlreadySet_KeepsFirstValue()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        active.SetBranchName(BranchName.From("feat/first-branch"));

        // Act
        active.SetBranchName(BranchName.From("feat/second-branch"));

        // Assert
        active.BranchName.ShouldBe(BranchName.From("feat/first-branch"));
    }

    [Fact]
    public void WhenNotCalled_BranchNameIsNull()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();

        // Act
        BranchName? result = active.BranchName;

        // Assert
        result.ShouldBeNull();
    }
}
