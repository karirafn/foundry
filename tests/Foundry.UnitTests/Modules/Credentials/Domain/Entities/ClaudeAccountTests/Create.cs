using Foundry.Modules.Credentials.Domain.Entities;
using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.Entities.ClaudeAccountTests;

public sealed class Create
{
    [Fact]
    public void WhenCreated_HasDefaultId()
    {
        // Arrange & Act
        ClaudeAccount account = ClaudeAccount.Create();

        // Assert
        account.Id.ShouldBe(ClaudeAccountId.Default);
    }

    [Fact]
    public void WhenCreated_CreatedAtIsSet()
    {
        // Arrange
        DateTimeOffset before = DateTimeOffset.UtcNow;

        // Act
        ClaudeAccount account = ClaudeAccount.Create();

        // Assert
        DateTimeOffset after = DateTimeOffset.UtcNow;
        account.CreatedAt.ShouldBeInRange(before, after);
    }

    [Fact]
    public void WhenCreated_UpdatedAtMatchesCreatedAt()
    {
        // Arrange & Act
        ClaudeAccount account = ClaudeAccount.Create();

        // Assert
        account.UpdatedAt.ShouldBe(account.CreatedAt);
    }
}
