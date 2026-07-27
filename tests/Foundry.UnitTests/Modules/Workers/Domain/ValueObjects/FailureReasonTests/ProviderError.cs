using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.FailureReasonTests;

public sealed class ProviderError
{
    [Fact]
    public void WhenConstructed_MessageIsPreserved()
    {
        // Arrange

        // Act
        FailureReason.ProviderError reason = new(Message: "Branch pre-creation on acme/repo returned 403");

        // Assert
        reason.Message.ShouldBe("Branch pre-creation on acme/repo returned 403");
    }

    [Fact]
    public void WhenTwoInstancesHaveSameMessage_AreEqual()
    {
        // Arrange
        FailureReason.ProviderError a = new(Message: "branch pre-creation failed");
        FailureReason.ProviderError b = new(Message: "branch pre-creation failed");

        // Act

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenTwoInstancesHaveDifferentMessage_AreNotEqual()
    {
        // Arrange
        FailureReason.ProviderError a = new(Message: "branch pre-creation failed");
        FailureReason.ProviderError b = new(Message: "PR creation failed");

        // Act

        // Assert
        a.ShouldNotBe(b);
    }
}
