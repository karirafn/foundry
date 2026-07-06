using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.CredentialValidityTests;

public sealed class ValueEquality
{
    [Fact]
    public void WhenTwoValidInstances_AreEqual()
    {
        // Arrange
        CredentialValidity a = new CredentialValidity.Valid();
        CredentialValidity b = new CredentialValidity.Valid();

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenTwoInvalidInstancesWithSameReason_AreEqual()
    {
        // Arrange
        CredentialValidity a = new CredentialValidity.Invalid("auth_failed");
        CredentialValidity b = new CredentialValidity.Invalid("auth_failed");

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenInvalidInstancesWithDifferentReasons_AreNotEqual()
    {
        // Arrange
        CredentialValidity a = new CredentialValidity.Invalid("reason_a");
        CredentialValidity b = new CredentialValidity.Invalid("reason_b");

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenValidAndInvalid_AreNotEqual()
    {
        // Arrange
        CredentialValidity a = new CredentialValidity.Valid();
        CredentialValidity b = new CredentialValidity.Invalid("some_reason");

        // Act
        bool result = a == b;

        // Assert
        result.ShouldBeFalse();
    }
}
