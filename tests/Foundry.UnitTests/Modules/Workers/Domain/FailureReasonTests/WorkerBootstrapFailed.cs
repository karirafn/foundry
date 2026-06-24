using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.FailureReasonTests;

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
        FailureReason.WorkerBootstrapFailed a = new(Detail: "npm install failed");
        FailureReason.WorkerBootstrapFailed b = new(Detail: "npm install failed");

        // Act

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenTwoInstancesHaveDifferentDetail_AreNotEqual()
    {
        // Arrange
        FailureReason.WorkerBootstrapFailed a = new(Detail: "git clone failed");
        FailureReason.WorkerBootstrapFailed b = new(Detail: "npm install failed");

        // Act

        // Assert
        a.ShouldNotBe(b);
    }
}
