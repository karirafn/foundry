using Foundry.Modules.Workers.Contracts;
using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.TokenUsageTests;

public sealed class Sum
{
    [Fact]
    public void WhenAllValuesKnown_ReturnsExactTotalWithNoUnknownRuns()
    {
        // Act
        TokenTotal sum = TokenUsage.Sum([10L, 20L, 30L]);

        // Assert
        sum.ShouldBe(new TokenTotal(Total: 60L, KnownRunCount: 3, UnknownRunCount: 0));
    }

    [Fact]
    public void WhenSomeValuesUnknown_SumsKnownValuesAndCountsUnknownRuns()
    {
        // Act
        TokenTotal sum = TokenUsage.Sum([10L, null, 30L, null]);

        // Assert
        sum.ShouldBe(new TokenTotal(Total: 40L, KnownRunCount: 2, UnknownRunCount: 2));
    }

    [Fact]
    public void WhenAllValuesUnknown_ReturnsZeroTotalWithOnlyUnknownRuns()
    {
        // Act
        TokenTotal sum = TokenUsage.Sum([null, null, null]);

        // Assert
        sum.ShouldBe(new TokenTotal(Total: 0L, KnownRunCount: 0, UnknownRunCount: 3));
    }

    [Fact]
    public void WhenNoValues_ReturnsAllZero()
    {
        // Act
        TokenTotal sum = TokenUsage.Sum([]);

        // Assert
        sum.ShouldBe(TokenTotal.None);
    }

    [Fact]
    public void WhenLegacyRunHasNullCacheBuckets_CountsPerMetric()
    {
        // Arrange — a legacy run: input and output known, cache columns null
        TokenUsage legacy = new(InputTokens: 100, CacheReadInputTokens: null, CacheCreationInputTokens: null, OutputTokens: 50);
        TokenUsage tracked = new(InputTokens: 10, CacheReadInputTokens: 20, CacheCreationInputTokens: 30, OutputTokens: 5);
        TokenUsage[] runs = [legacy, tracked];

        // Act
        TokenTotal input = TokenUsage.Sum(runs.Select(r => r.BillableInputTokens));
        TokenTotal output = TokenUsage.Sum(runs.Select(r => r.OutputTokens));

        // Assert
        input.ShouldBe(new TokenTotal(Total: 60L, KnownRunCount: 1, UnknownRunCount: 1));
        output.ShouldBe(new TokenTotal(Total: 55L, KnownRunCount: 2, UnknownRunCount: 0));
    }
}
