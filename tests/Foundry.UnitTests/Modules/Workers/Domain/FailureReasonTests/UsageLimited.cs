using System;
using System.Text.Json;

using Foundry.Modules.Workers.Domain;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Workers.Domain.FailureReasonTests;

public sealed class UsageLimited
{
    [Fact]
    public void WhenConstructed_ResetsAtIsPreserved()
    {
        // Arrange
        DateTimeOffset resetsAt = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);

        // Act
        FailureReason.UsageLimited reason = new(ResetsAt: resetsAt);

        // Assert
        reason.ResetsAt.ShouldBe(resetsAt);
    }

    [Fact]
    public void WhenTwoInstancesHaveSameResetsAt_AreEqual()
    {
        // Arrange
        DateTimeOffset resetsAt = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        FailureReason.UsageLimited a = new(ResetsAt: resetsAt);
        FailureReason.UsageLimited b = new(ResetsAt: resetsAt);

        // Act

        // Assert
        a.ShouldBe(b);
    }

    [Fact]
    public void WhenTwoInstancesHaveDifferentResetsAt_AreNotEqual()
    {
        // Arrange
        FailureReason.UsageLimited a = new(ResetsAt: new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero));
        FailureReason.UsageLimited b = new(ResetsAt: new DateTimeOffset(2026, 6, 19, 12, 0, 0, TimeSpan.Zero));

        // Act

        // Assert
        a.ShouldNotBe(b);
    }

    [Fact]
    public void WhenSerialized_ContainsUsageLimitedDiscriminator()
    {
        // Arrange
        FailureReason reason = new FailureReason.UsageLimited(
            ResetsAt: new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero));

        // Act
        string json = JsonSerializer.Serialize(reason);

        // Assert
        json.ShouldContain(@"""$type"":""usage_limited""");
    }

    [Fact]
    public void WhenSerialized_ContainsResetsAt()
    {
        // Arrange
        DateTimeOffset resetsAt = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        FailureReason reason = new FailureReason.UsageLimited(ResetsAt: resetsAt);

        // Act
        string json = JsonSerializer.Serialize(reason);

        // Assert
        json.ShouldContain("ResetsAt");
    }

    [Fact]
    public void WhenRoundTripped_ResetsAtIsPreserved()
    {
        // Arrange
        DateTimeOffset resetsAt = new DateTimeOffset(2026, 6, 18, 12, 0, 0, TimeSpan.Zero);
        FailureReason original = new FailureReason.UsageLimited(ResetsAt: resetsAt);

        // Act
        string json = JsonSerializer.Serialize(original);
        FailureReason? result = JsonSerializer.Deserialize<FailureReason>(json);

        // Assert
        FailureReason.UsageLimited usageLimited = result.ShouldBeOfType<FailureReason.UsageLimited>();
        usageLimited.ResetsAt.ShouldBe(resetsAt);
    }
}
