using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class WorkerBootstrapFailed
{
    [Fact]
    public void WhenConstructed_DetailIsPreserved()
    {
        // Arrange

        // Act
        FailureReason.WorkerBootstrapFailed reason = new(Detail: "git clone failed: exit 128");

        // Assert
        reason.Detail.ShouldBe("git clone failed: exit 128");
    }

    [Fact]
    public void WhenTwoInstancesHaveSameDetail_AreEqual()
    {
        // Arrange
        FailureReason.WorkerBootstrapFailed first = new(Detail: "npm install failed");
        FailureReason.WorkerBootstrapFailed second = new(Detail: "npm install failed");

        // Act
        bool areEqual = first == second;

        // Assert
        areEqual.ShouldBeTrue();
    }

    [Fact]
    public void WhenTwoInstancesHaveDifferentDetail_AreNotEqual()
    {
        // Arrange
        FailureReason.WorkerBootstrapFailed first = new(Detail: "git clone failed");
        FailureReason.WorkerBootstrapFailed second = new(Detail: "npm install failed");

        // Act
        bool areEqual = first == second;

        // Assert
        areEqual.ShouldBeFalse();
    }
}
