using Foundry.Modules.Credentials.Features.Usage;
using Foundry.Testing;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Credentials.Features.Usage.UsageSnapshotBuilderTests;

public sealed class Build
{
    [Fact]
    public void WhenUsingDefaults_ReturnsValidSnapshot()
    {
        // Arrange
        UsageSnapshotBuilder builder = new();

        // Act
        UsageSnapshot snapshot = builder.Build();

        // Assert
        snapshot.ShouldSatisfyAllConditions(
            () => snapshot.AccountEmail.ShouldNotBeNullOrEmpty(),
            () => snapshot.Limits.ShouldNotBeEmpty(),
            () => snapshot.CapturedAt.ShouldBeGreaterThan(DateTimeOffset.MinValue),
            () => snapshot.LastRefreshFailedAt.ShouldBeNull());
    }

    [Fact]
    public void WhenAccountEmailOverridden_UsesOverriddenValue()
    {
        // Arrange
        UsageSnapshotBuilder builder = new UsageSnapshotBuilder()
            .WithAccountEmail("custom@example.com");

        // Act
        UsageSnapshot snapshot = builder.Build();

        // Assert
        snapshot.AccountEmail.ShouldBe("custom@example.com");
    }

    [Fact]
    public void WhenExtraUsageOverridden_UsesOverriddenValue()
    {
        // Arrange
        UsageExtraCredits extraCredits = new(
            IsEnabled: true,
            MonthlyLimit: 50000L,
            UsedCredits: 12500L,
            Utilization: 0.25m,
            Currency: "USD");

        UsageSnapshotBuilder builder = new UsageSnapshotBuilder()
            .WithExtraUsage(extraCredits);

        // Act
        UsageSnapshot snapshot = builder.Build();

        // Assert
        UsageExtraCredits credits = snapshot.ExtraUsage.ShouldNotBeNull();
        credits.ShouldSatisfyAllConditions(
            () => credits.IsEnabled.ShouldBeTrue(),
            () => credits.MonthlyLimit.ShouldBe(50000L),
            () => credits.UsedCredits.ShouldBe(12500L),
            () => credits.Utilization.ShouldBe(0.25m),
            () => credits.Currency.ShouldBe("USD"));
    }
}
