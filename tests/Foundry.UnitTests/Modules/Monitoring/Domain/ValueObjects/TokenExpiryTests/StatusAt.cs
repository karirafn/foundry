using Foundry.Modules.Monitoring.Domain.ValueObjects;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Domain.ValueObjects.TokenExpiryTests;

public sealed class StatusAt
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WhenTheExpiryIsNow_ReturnsExpired()
    {
        // Act
        TokenExpiryStatus status = new TokenExpiry.On(Now).StatusAt(Now);

        // Assert
        status.ShouldBe(TokenExpiryStatus.Expired);
    }

    [Fact]
    public void WhenTheExpiryPassedOneSecondAgo_ReturnsExpired()
    {
        // Act
        TokenExpiryStatus status = new TokenExpiry.On(Now.AddSeconds(-1)).StatusAt(Now);

        // Assert
        status.ShouldBe(TokenExpiryStatus.Expired);
    }

    [Fact]
    public void WhenTheExpiryIsExactlyTheWarningWindowAway_ReturnsExpiringSoon()
    {
        // Act
        TokenExpiryStatus status = new TokenExpiry.On(Now.AddDays(14)).StatusAt(Now);

        // Assert
        status.ShouldBe(TokenExpiryStatus.ExpiringSoon);
    }

    [Fact]
    public void WhenTheExpiryIsOneSecondBeyondTheWarningWindow_ReturnsCurrent()
    {
        // Act
        TokenExpiryStatus status = new TokenExpiry.On(Now.AddDays(14).AddSeconds(1)).StatusAt(Now);

        // Assert
        status.ShouldBe(TokenExpiryStatus.Current);
    }

    [Fact]
    public void WhenNotChecked_ReturnsNotChecked()
    {
        // Act
        TokenExpiryStatus status = new TokenExpiry.NotChecked().StatusAt(Now);

        // Assert
        status.ShouldBe(TokenExpiryStatus.NotChecked);
    }

    [Fact]
    public void WhenUnavailable_ReturnsUnavailable()
    {
        // Act
        TokenExpiryStatus status = new TokenExpiry.Unavailable().StatusAt(Now);

        // Assert
        status.ShouldBe(TokenExpiryStatus.Unavailable);
    }

    [Fact]
    public void WhenNever_ReturnsNever()
    {
        // Act
        TokenExpiryStatus status = new TokenExpiry.Never().StatusAt(Now);

        // Assert
        status.ShouldBe(TokenExpiryStatus.Never);
    }
}
