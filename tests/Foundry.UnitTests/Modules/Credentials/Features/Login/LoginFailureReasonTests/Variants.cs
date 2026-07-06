using Foundry.Modules.Credentials.Features.Login;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Login.LoginFailureReasonTests;

public sealed class Variants
{
    [Fact]
    public void InvalidCode_CanBeCreated_WithOptionalMessage()
    {
        // Arrange / Act
        LoginFailureReason reason = new LoginFailureReason.InvalidCode("The code was wrong.");

        // Assert
        LoginFailureReason.InvalidCode invalidCode = reason.ShouldBeOfType<LoginFailureReason.InvalidCode>();
        invalidCode.Message.ShouldBe("The code was wrong.");
    }

    [Fact]
    public void UrlTimeout_CanBeCreated_WithNullMessage()
    {
        // Arrange / Act
        LoginFailureReason reason = new LoginFailureReason.UrlTimeout();

        // Assert
        LoginFailureReason.UrlTimeout urlTimeout = reason.ShouldBeOfType<LoginFailureReason.UrlTimeout>();
        urlTimeout.Message.ShouldBeNull();
    }

    [Fact]
    public void CodeTimeout_CanBeCreated_WithOptionalMessage()
    {
        // Arrange / Act
        LoginFailureReason reason = new LoginFailureReason.CodeTimeout("Timed out after 10 minutes.");

        // Assert
        LoginFailureReason.CodeTimeout codeTimeout = reason.ShouldBeOfType<LoginFailureReason.CodeTimeout>();
        codeTimeout.Message.ShouldBe("Timed out after 10 minutes.");
    }

    [Fact]
    public void Unknown_CanBeCreated_WithOptionalMessage()
    {
        // Arrange / Act
        LoginFailureReason reason = new LoginFailureReason.Unknown("Unexpected exception.");

        // Assert
        LoginFailureReason.Unknown unknown = reason.ShouldBeOfType<LoginFailureReason.Unknown>();
        unknown.Message.ShouldBe("Unexpected exception.");
    }

    [Fact]
    public void FailedPhase_CarriesTypedReason()
    {
        // Arrange
        LoginFailureReason reason = new LoginFailureReason.InvalidCode("bad code");

        // Act
        LoginPhase phase = new LoginPhase.Failed(reason);

        // Assert
        LoginPhase.Failed failed = phase.ShouldBeOfType<LoginPhase.Failed>();
        failed.Reason.ShouldBeOfType<LoginFailureReason.InvalidCode>();
    }
}
