using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.WebApi.UnitTests.Modules.Workers.Domain.FailureReasonTests;

public sealed class ContainerError
{
    [Fact]
    public void WhenConstructed_MessageIsPreserved()
    {
        // Arrange

        // Act
        FailureReason.ContainerError reason = new(Message: "container failed to start");

        // Assert
        reason.Message.ShouldBe("container failed to start");
    }

    [Fact]
    public void WhenTwoInstancesHaveSameMessage_AreEqual()
    {
        // Arrange
        FailureReason.ContainerError a = new(Message: "oom killed");
        FailureReason.ContainerError b = new(Message: "oom killed");

        // Act

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenTwoInstancesHaveDifferentMessage_AreNotEqual()
    {
        // Arrange
        FailureReason.ContainerError a = new(Message: "oom killed");
        FailureReason.ContainerError b = new(Message: "image not found");

        // Act

        // Assert
        a.ShouldNotBe(b);
    }
}
