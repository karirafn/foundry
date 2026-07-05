using Foundry.Modules.Credentials.Domain;
using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.ClaudeAccountTests;

public sealed class Invalidate
{
    [Fact]
    public void WhenCalled_SetsValidityToInvalid()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey("key"));

        // Act
        account.Invalidate("worker_auth_failed");

        // Assert
        CredentialValidity.Invalid invalid = account.Validity.ShouldBeOfType<CredentialValidity.Invalid>();
        invalid.Reason.ShouldBe("worker_auth_failed");
    }

    [Fact]
    public void WhenCalled_ReturnsTrueIndicatingStateChanged()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey("key"));

        // Act
        bool changed = account.Invalidate("worker_auth_failed");

        // Assert
        changed.ShouldBeTrue();
    }

    [Fact]
    public void WhenAlreadyInvalid_IsIdempotentAndReturnsFalse()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey("key"));
        account.Invalidate("first_failure");

        // Act
        bool changed = account.Invalidate("second_failure");

        // Assert
        changed.ShouldBeFalse();
    }

    [Fact]
    public void WhenAlreadyInvalid_PreservesOriginalReason()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey("key"));
        account.Invalidate("original_reason");

        // Act
        account.Invalidate("new_reason");

        // Assert
        CredentialValidity.Invalid invalid = account.Validity.ShouldBeOfType<CredentialValidity.Invalid>();
        invalid.Reason.ShouldBe("original_reason");
    }

    [Fact]
    public void WhenCalled_UpdatesUpdatedAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey("key"));
        DateTimeOffset before = account.UpdatedAt;

        // Act
        account.Invalidate("worker_auth_failed");

        // Assert
        account.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenAlreadyInvalid_DoesNotUpdateUpdatedAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey("key"));
        account.Invalidate("first_reason");
        DateTimeOffset updatedAt = account.UpdatedAt;

        // Act
        account.Invalidate("second_reason");

        // Assert
        account.UpdatedAt.ShouldBe(updatedAt);
    }
}
