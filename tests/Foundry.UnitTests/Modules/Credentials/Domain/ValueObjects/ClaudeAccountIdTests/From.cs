using Foundry.Modules.Credentials.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Domain.ValueObjects.ClaudeAccountIdTests;

public sealed class From
{
    [Fact]
    public void WhenGivenGuid_ReturnsClaudeAccountIdWithSameValue()
    {
        // Arrange
        Guid guid = Guid.NewGuid();

        // Act
        ClaudeAccountId id = ClaudeAccountId.From(guid);

        // Assert
        id.Value.ShouldBe(guid);
    }

    [Fact]
    public void WhenDefaultCalled_ReturnsWellKnownSingletonId()
    {
        // Arrange
        Guid expected = new("00000000-0000-0000-0000-000000000002");

        // Act
        ClaudeAccountId id = ClaudeAccountId.Default;

        // Assert
        id.Value.ShouldBe(expected);
    }
}
