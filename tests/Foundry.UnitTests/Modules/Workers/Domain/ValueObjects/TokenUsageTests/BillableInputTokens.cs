using Foundry.Modules.Workers.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.ValueObjects.TokenUsageTests;

public sealed class BillableInputTokens
{
    [Fact]
    public void WhenAllInputBucketsKnown_SumsUncachedCacheReadAndCacheWrite()
    {
        // Arrange
        TokenUsage sut = new(InputTokens: 46, CacheReadInputTokens: 1_018_829, CacheCreationInputTokens: 51_565, OutputTokens: 700);

        // Act
        long? billable = sut.BillableInputTokens;

        // Assert
        billable.ShouldBe(1_070_440L);
    }

    [Theory]
    [InlineData(null, 1L, 1L)]
    [InlineData(1L, null, 1L)]
    [InlineData(1L, 1L, null)]
    public void WhenAnyInputBucketUnknown_ReturnsNull(long? input, long? cacheRead, long? cacheCreation)
    {
        // Arrange — e.g. a legacy row persisted before the cache columns existed
        TokenUsage sut = new(input, cacheRead, cacheCreation, OutputTokens: 5);

        // Act
        long? billable = sut.BillableInputTokens;

        // Assert
        billable.ShouldBeNull();
    }

    [Fact]
    public void WhenOutputUnknown_StillSumsInput()
    {
        // Arrange
        TokenUsage sut = new(1, 2, 3, OutputTokens: null);

        // Act & Assert
        sut.BillableInputTokens.ShouldBe(6L);
    }
}
