using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.AutomaticRetryScheduleTests;

public sealed class Create
{
    [Fact]
    public void WhenNoBackoffs_Throws()
    {
        // Act / Assert
        Should.Throw<ArgumentException>(() => AutomaticRetrySchedule.Create());
    }

    [Fact]
    public void WhenABackoffIsNotPositive_Throws()
    {
        // Act / Assert
        Should.Throw<ArgumentException>(
            () => AutomaticRetrySchedule.Create(TimeSpan.FromMinutes(1), TimeSpan.Zero));
    }

    [Fact]
    public void MaxRetries_IsTheNumberOfBackoffs()
    {
        // Act
        AutomaticRetrySchedule schedule = AutomaticRetrySchedule.Create(
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(3));

        // Assert
        schedule.MaxRetries.ShouldBe(3);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    public void IsExhausted_WhenTheStreakReachesTheMaximum(int consecutiveFailures, bool expected)
    {
        // Arrange
        AutomaticRetrySchedule schedule = AutomaticRetrySchedule.Create(
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2));

        // Act
        bool exhausted = schedule.IsExhausted(consecutiveFailures);

        // Assert
        exhausted.ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(9, 2)]
    public void BackoffAfter_UsesTheBackoffOfTheNextRetryClampedToTheSchedule(int consecutiveFailures, int minutes)
    {
        // Arrange
        AutomaticRetrySchedule schedule = AutomaticRetrySchedule.Create(
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2));

        // Act
        TimeSpan backoff = schedule.BackoffAfter(consecutiveFailures);

        // Assert
        backoff.ShouldBe(TimeSpan.FromMinutes(minutes));
    }
}
