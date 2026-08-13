using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.Entities.ClaudeAccountTests;

public sealed class BlockSpend
{
    private static readonly DateTimeOffset SomeProbeAt = DateTimeOffset.UtcNow.AddHours(1);

    [Fact]
    public void WhenSpendIsAvailable_TransitionsToBlockedAndReturnsTrue()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        bool changed = account.BlockSpend(SomeProbeAt);

        // Assert
        account.SpendState.ShouldBeOfType<SpendState.Blocked>();
        changed.ShouldBeTrue();
    }

    [Fact]
    public void WhenSpendIsAvailable_SetsNextProbeAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        DateTimeOffset expectedProbeAt = DateTimeOffset.UtcNow.AddMinutes(60);

        // Act
        account.BlockSpend(expectedProbeAt);

        // Assert
        SpendState.Blocked blocked = account.SpendState.ShouldBeOfType<SpendState.Blocked>();
        blocked.NextProbeAt.ShouldBe(expectedProbeAt);
    }

    [Fact]
    public void WhenSpendAlreadyBlocked_IsIdempotentAndReturnsFalse()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend(SomeProbeAt);

        // Act
        bool changed = account.BlockSpend(SomeProbeAt.AddHours(1));

        // Assert
        changed.ShouldBeFalse();
    }

    [Fact]
    public void WhenSpendAlreadyBlocked_PreservesExistingProbeAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        DateTimeOffset originalProbeAt = DateTimeOffset.UtcNow.AddHours(1);
        account.BlockSpend(originalProbeAt);

        // Act
        account.BlockSpend(originalProbeAt.AddHours(1));

        // Assert
        SpendState.Blocked blocked = account.SpendState.ShouldBeOfType<SpendState.Blocked>();
        blocked.NextProbeAt.ShouldBe(originalProbeAt);
    }

    [Fact]
    public void WhenSpendBlocked_DoesNotChangeValidity()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        account.BlockSpend(SomeProbeAt);

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
        account.BlockSpend(SomeProbeAt);

        // Assert
        account.UpdatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public void WhenSpendAlreadyBlocked_DoesNotUpdateUpdatedAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend(SomeProbeAt);
        DateTimeOffset updatedAt = account.UpdatedAt;

        // Act
        account.BlockSpend(SomeProbeAt);

        // Assert
        account.UpdatedAt.ShouldBe(updatedAt);
    }

    [Fact]
    public void WhenSpendRestored_TransitionsToAvailableAndReturnsTrue()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend(SomeProbeAt);

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
        account.BlockSpend(SomeProbeAt);
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

    [Fact]
    public void WhenRearmProbe_OnBlockedAccount_UpdatesNextProbeAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        account.BlockSpend(SomeProbeAt);
        DateTimeOffset newProbeAt = SomeProbeAt.AddHours(1);

        // Act
        bool changed = account.RearmProbe(newProbeAt);

        // Assert
        changed.ShouldBeTrue();
        SpendState.Blocked blocked = account.SpendState.ShouldBeOfType<SpendState.Blocked>();
        blocked.NextProbeAt.ShouldBe(newProbeAt);
    }

    [Fact]
    public void WhenRearmProbe_OnAvailableAccount_IsNoOpAndReturnsFalse()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();

        // Act
        bool changed = account.RearmProbe(SomeProbeAt);

        // Assert
        changed.ShouldBeFalse();
        account.SpendState.ShouldBeOfType<SpendState.Available>();
    }

    [Fact]
    public void WhenRearmProbe_OnAvailableAccount_DoesNotUpdateUpdatedAt()
    {
        // Arrange
        ClaudeAccount account = ClaudeAccount.Create();
        DateTimeOffset updatedAt = account.UpdatedAt;

        // Act
        account.RearmProbe(SomeProbeAt);

        // Assert
        account.UpdatedAt.ShouldBe(updatedAt);
    }
}
