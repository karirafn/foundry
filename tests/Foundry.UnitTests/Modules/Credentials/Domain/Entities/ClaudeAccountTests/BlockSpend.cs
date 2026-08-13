using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.Entities.ClaudeAccountTests;

public sealed class BlockSpend
{
    [Fact]
    public void WhenSpendIsAvailable_TransitionsToBlockedAndReturnsTrue()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        bool changed = account.BlockSpend();

        // Assert
        account.SpendState.ShouldBeOfType<SpendState.Blocked>();
        changed.ShouldBeTrue();
    }

    [Fact]
    public void WhenSpendAlreadyBlocked_IsIdempotentAndReturnsFalse()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend();

        // Act
        bool changed = account.BlockSpend();

        // Assert
        changed.ShouldBeFalse();
    }

    [Fact]
    public void WhenSpendBlocked_DoesNotChangeValidity()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        account.BlockSpend();

        // Assert
        account.Validity.ShouldBeOfType<CredentialValidity.Valid>();
    }

    [Fact]
    public void WhenSpendBlocked_UpdatesUpdatedAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        DateTimeOffset before = account.UpdatedAt;

        // Act
        account.BlockSpend();

        // Assert
        account.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenSpendAlreadyBlocked_DoesNotUpdateUpdatedAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend();
        DateTimeOffset updatedAt = account.UpdatedAt;

        // Act
        account.BlockSpend();

        // Assert
        account.UpdatedAt.ShouldBe(updatedAt);
    }

    [Fact]
    public void WhenSpendRestored_TransitionsToAvailableAndReturnsTrue()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend();

        // Act
        bool changed = account.RestoreSpend();

        // Assert
        account.SpendState.ShouldBeOfType<SpendState.Available>();
        changed.ShouldBeTrue();
    }

    [Fact]
    public void WhenSpendAlreadyAvailable_RestoreIsIdempotentAndReturnsFalse()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        bool changed = account.RestoreSpend();

        // Assert
        changed.ShouldBeFalse();
    }

    [Fact]
    public void WhenSpendRestored_DoesNotChangeValidity()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend();
        account.Invalidate("some_reason");

        // Act
        account.RestoreSpend();

        // Assert
        account.Validity.ShouldBeOfType<CredentialValidity.Invalid>();
    }

    [Fact]
    public void WhenCreated_SpendStateIsAvailable()
    {
        // Arrange & Act
        ClaudeAccount account = ClaudeAccount.Create();

        // Assert
        account.SpendState.ShouldBeOfType<SpendState.Available>();
    }
}
