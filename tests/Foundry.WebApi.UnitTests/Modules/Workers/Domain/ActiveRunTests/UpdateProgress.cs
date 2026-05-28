using Foundry.WebApi.Modules.Issues.Domain;
using Foundry.WebApi.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Domain.ActiveRunTests;

public sealed class UpdateProgress
{
    private static ActiveRun CreateActiveRun()
    {
        StartingRun starting = StartingRun.Begin(IssueId.New());
        return starting.Activate("container-123");
    }

    [Fact]
    public void WhenCalled_SetsLatestProgress()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();

        // Act
        active.UpdateProgress("Step 1 of 5 complete");

        // Assert
        active.LatestProgress.ShouldBe("Step 1 of 5 complete");
    }

    [Fact]
    public void WhenCalledMultipleTimes_OverwritesPreviousProgress()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();
        active.UpdateProgress("Step 1 of 5 complete");

        // Act
        active.UpdateProgress("Step 2 of 5 complete");

        // Assert
        active.LatestProgress.ShouldBe("Step 2 of 5 complete");
    }

    [Fact]
    public void WhenNotCalled_LatestProgressIsNull()
    {
        // Arrange
        ActiveRun active = CreateActiveRun();

        // Act / Assert
        active.LatestProgress.ShouldBeNull();
    }
}
