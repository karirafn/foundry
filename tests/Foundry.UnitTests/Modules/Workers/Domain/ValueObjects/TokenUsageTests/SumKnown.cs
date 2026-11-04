using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.TokenUsageTests;

public sealed class SumKnown
{
    [Fact]
    public void WhenAllValuesKnown_ReturnsSum()
    {
        // Act
        long? sum = TokenUsage.SumKnown([10L, 20L, 30L]);

        // Assert
        sum.ShouldBe(60L);
    }

    [Fact]
    public void WhenAnyValueUnknown_ReturnsNull()
    {
        // Act
        long? sum = TokenUsage.SumKnown([10L, null, 30L]);

        // Assert
        sum.ShouldBeNull();
    }

    [Fact]
    public void WhenNoValues_ReturnsNull()
    {
        // Act
        long? sum = TokenUsage.SumKnown([]);

        // Assert
        sum.ShouldBeNull();
    }
}
