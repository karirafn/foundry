using Foundry.Modules.Issues.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Issues.Domain.ValueObjects;

public sealed class RebasePendingReasonTests
{
    public static TheoryData<RebasePendingReason, string> Variants => new()
    {
        { new RebasePendingReason.AfterRun(), "after_run" },
        { new RebasePendingReason.CatchUp(), "catch_up" },
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public void Discriminator_RoundTrips(RebasePendingReason reason, string discriminator)
    {
        // Act
        RebasePendingReason? rebuilt = RebasePendingReason.FromDiscriminator(reason.Discriminator);

        // Assert
        reason.Discriminator.ShouldBe(discriminator);
        rebuilt.ShouldBe(reason);
    }

    [Fact]
    public void FromDiscriminator_WhenUnknown_ReturnsNull()
    {
        // Act & Assert
        RebasePendingReason.FromDiscriminator("unknown").ShouldBeNull();
    }

    [Fact]
    public void ChecksPendingCatchUp_RoundTrips()
    {
        // Act
        ChecksPendingReason reason = new ChecksPendingReason.CatchUp();

        // Assert
        reason.Discriminator.ShouldBe("catch_up");
        ChecksPendingReason.FromDiscriminator("catch_up").ShouldBe(reason);
    }
}
