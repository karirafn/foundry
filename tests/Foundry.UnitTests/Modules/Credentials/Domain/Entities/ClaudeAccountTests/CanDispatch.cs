using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.Entities.ClaudeAccountTests;

public sealed class CanDispatch
{
    [Fact]
    public void WhenOAuthModeAndValidAndAvailable_ReturnsTrue()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyPresentAndValidAndAvailable_ReturnsTrue()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("sk-ant-abc")));

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void WhenApiKeyNotConfigured_ReturnsFalse()
    {
        // Arrange — Create() seeds NotConfigured by default.
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenValidityIsInvalid_ReturnsFalse()
    {
        // Arrange — OAuth so key check passes; validity check fires.
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
        account.Invalidate("worker_auth_failed");

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenSpendIsBlocked_ReturnsFalse()
    {
        // Arrange — OAuth + valid; spend check fires.
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
        account.BlockSpend(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenValidityInvalidAndSpendBlocked_ReturnsFalse()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.RecordSuccessfulLogin("user@example.com", "MyOrg", "pro");
        account.Invalidate("worker_auth_failed");
        account.BlockSpend(DateTimeOffset.UtcNow.AddHours(1));

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void WhenApiKeyPresentButInvalid_ReturnsFalse()
    {
        // Arrange — key is Present so key check passes; validity check fires.
        ClaudeAccount account = ClaudeAccount.Create();
        account.SetAuthMode(new AuthMode.ApiKey(new ApiKeyCredential.Present("sk-ant-abc")));
        account.Invalidate("worker_auth_failed");

        // Act
        bool result = account.CanDispatch;

        // Assert
        result.ShouldBeFalse();
    }
}
