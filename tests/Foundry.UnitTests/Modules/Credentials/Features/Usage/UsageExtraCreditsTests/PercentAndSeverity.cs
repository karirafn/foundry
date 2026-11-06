using Foundry.Modules.Credentials.Features.Usage;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.UsageExtraCreditsTests;

public sealed class PercentAndSeverity
{
    private static UsageExtraCredits Credits(long? monthlyLimit, long usedCredits, decimal? utilization) =>
        new(
            IsEnabled: true,
            MonthlyLimit: monthlyLimit,
            UsedCredits: usedCredits,
            Utilization: utilization,
            Currency: "USD");

    [Fact]
    public void WhenUtilizationIsNull_DerivesPercentFromUsedOverLimit()
    {
        // Arrange
        UsageExtraCredits credits = Credits(monthlyLimit: 50000, usedCredits: 16568, utilization: null);

        // Act
        decimal? percent = credits.Percent;

        // Assert
        percent.ShouldBe(33.136m);
    }

    [Fact]
    public void WhenUtilizationIsPresent_PercentIsTheUtilization()
    {
        // Arrange
        UsageExtraCredits credits = Credits(monthlyLimit: 50000, usedCredits: 16568, utilization: 41m);

        // Act
        decimal? percent = credits.Percent;

        // Assert
        percent.ShouldBe(41m);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public void WhenMonthlyLimitIsNullOrZero_PercentAndSeverityAreNull(long? monthlyLimit)
    {
        // Arrange
        UsageExtraCredits credits = Credits(monthlyLimit: monthlyLimit, usedCredits: 1200, utilization: 12m);

        // Act / Assert
        credits.ShouldSatisfyAllConditions(
            () => credits.Percent.ShouldBeNull(),
            () => credits.Severity.ShouldBeNull());
    }

    [Theory]
    [InlineData(0L, "normal")]
    [InlineData(7999L, "normal")]
    [InlineData(8000L, "warning")]
    [InlineData(9999L, "warning")]
    [InlineData(10000L, "critical")]
    [InlineData(15000L, "critical")]
    public void WhenPercentIsKnown_SeverityFollowsThresholds(long usedCredits, string expectedSeverity)
    {
        // Arrange
        UsageExtraCredits credits = Credits(monthlyLimit: 10000, usedCredits: usedCredits, utilization: null);

        // Act
        string? severity = credits.Severity;

        // Assert
        severity.ShouldBe(expectedSeverity);
    }

    [Fact]
    public void WhenUsedExceedsLimit_PercentIsAboveHundred()
    {
        // Arrange
        UsageExtraCredits credits = Credits(monthlyLimit: 10000, usedCredits: 12500, utilization: null);

        // Act / Assert
        credits.Percent.ShouldBe(125m);
    }
}
