using Foundry.Modules.Workers.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Contracts.FailureCategoryTests;

public sealed class AutomaticRetry
{
    [Fact]
    public void TransientApiError_AllowsTwoRetries()
    {
        // Act
        AutomaticRetrySchedule? schedule = FailureCategory.TransientApiError.AutomaticRetry;

        // Assert
        schedule.ShouldNotBeNull().MaxRetries.ShouldBe(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TransientApiError_BacksOffOneMinuteBeforeEachRetry(int consecutiveFailures)
    {
        // Act
        TimeSpan backoff = FailureCategory.TransientApiError.AutomaticRetry!.BackoffAfter(consecutiveFailures);

        // Assert
        backoff.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void ProviderUnreachable_AllowsFiveRetries()
    {
        // Act
        AutomaticRetrySchedule? schedule = FailureCategory.ProviderUnreachable.AutomaticRetry;

        // Assert
        schedule.ShouldNotBeNull().MaxRetries.ShouldBe(5);
    }

    [Fact]
    public void ProviderUnreachable_BacksOffDoublingFromTwoMinutes()
    {
        // Act
        TimeSpan[] backoffs = [.. FailureCategory.ProviderUnreachable.AutomaticRetry!.Backoffs];

        // Assert
        backoffs.ShouldBe(
        [
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(4),
            TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(16),
            TimeSpan.FromMinutes(32),
        ]);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    public void ProviderUnreachable_BackoffAfterAStreakIsTheNextRetrysBackoff(int consecutiveFailures, int minutes)
    {
        // Act
        TimeSpan backoff = FailureCategory.ProviderUnreachable.AutomaticRetry!.BackoffAfter(consecutiveFailures);

        // Assert
        backoff.ShouldBe(TimeSpan.FromMinutes(minutes));
    }

    public static TheoryData<string> NonRetryableTokens =>
    [
        FailureCategory.NonZeroExitToken,
        FailureCategory.TimedOutToken,
        FailureCategory.ContainerErrorToken,
        FailureCategory.UsageLimitedToken,
        FailureCategory.WorkerBootstrapFailedToken,
        FailureCategory.AuthInvalidToken,
        FailureCategory.ProviderErrorToken,
        FailureCategory.CreditsExhaustedToken,
        FailureCategory.ModelUnavailableToken,
        FailureCategory.PrClosedToken,
        FailureCategory.CancelledToken,
        FailureCategory.TurnLimitToken,
        FailureCategory.VerdictInvalidToken,
        FailureCategory.PublishFailedToken,
        FailureCategory.PublishExpiredToken,
        FailureCategory.RunDiscardedToken,
        FailureCategory.BlockerCycleToken,
        FailureCategory.BlockerLinkFailedToken,
        FailureCategory.IssueClosedToken,
    ];

    [Theory]
    [MemberData(nameof(NonRetryableTokens))]
    public void EveryOtherCategory_HasNoSchedule(string token)
    {
        // Act
        FailureCategory category = FailureCategory.FromToken(token);

        // Assert
        category.AutomaticRetry.ShouldBeNull();
    }

    [Fact]
    public void AutoRetryable_IsEveryCategoryWithASchedule()
    {
        // Act
        IReadOnlyCollection<FailureCategory> autoRetryable = FailureCategory.AutoRetryable;

        // Assert
        autoRetryable.ShouldBe(
            [FailureCategory.ProviderUnreachable, FailureCategory.TransientApiError],
            ignoreOrder: true);
    }

    [Theory]
    [InlineData(FailureCategory.TransientApiErrorToken, true)]
    [InlineData(FailureCategory.ProviderUnreachableToken, true)]
    [InlineData(FailureCategory.NonZeroExitToken, false)]
    [InlineData("not_a_category", false)]
    public void IsAutoRetryableToken_IsTrueOnlyForATokenWhoseCategoryHasASchedule(string token, bool expected)
    {
        // Act
        bool result = FailureCategory.IsAutoRetryableToken(token);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void ShortestFirstAutomaticRetryBackoff_IsTheSmallestFirstBackoffOfAnyAutoRetryableCategory()
    {
        // Act
        TimeSpan shortest = FailureCategory.ShortestFirstAutomaticRetryBackoff;

        // Assert
        shortest.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [Fact]
    public void IsAutoRetryableToken_IsFalseForNull()
    {
        // Act
        bool result = FailureCategory.IsAutoRetryableToken(null);

        // Assert
        result.ShouldBeFalse();
    }
}
