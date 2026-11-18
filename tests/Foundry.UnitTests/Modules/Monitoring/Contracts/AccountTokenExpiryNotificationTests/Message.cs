using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.AccountTokenExpiryNotificationTests;

public sealed class Message
{
    [Fact]
    public void WhenNoAccountIsExpiringOrExpired_ReturnsEmpty()
    {
        // Act
        string message = AccountTokenExpiryNotification.Message(expiringSoon: 0, expired: 0);

        // Assert
        message.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(1, 0, "1 account has a token expiring within 14 days — enter a new one before monitoring stops.")]
    [InlineData(2, 0, "2 accounts have tokens expiring within 14 days — enter new ones before monitoring stops.")]
    [InlineData(0, 1, "1 account has an expired token — enter a new one before monitoring stops.")]
    [InlineData(0, 2, "2 accounts have expired tokens — enter new ones before monitoring stops.")]
    [InlineData(1, 1, "1 account has an expired token and 1 has a token expiring within 14 days — enter new ones before monitoring stops.")]
    [InlineData(2, 1, "1 account has an expired token and 2 have tokens expiring within 14 days — enter new ones before monitoring stops.")]
    [InlineData(1, 2, "2 accounts have expired tokens and 1 has a token expiring within 14 days — enter new ones before monitoring stops.")]
    [InlineData(3, 2, "2 accounts have expired tokens and 3 have tokens expiring within 14 days — enter new ones before monitoring stops.")]
    public void WhenAccountsAreExpiringOrExpired_ReturnsTheCopyForTheCounts(int expiringSoon, int expired, string expected)
    {
        // Act
        string message = AccountTokenExpiryNotification.Message(expiringSoon, expired);

        // Assert
        message.ShouldBe(expected);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void WhenACountIsNegative_Throws(int expiringSoon, int expired)
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => AccountTokenExpiryNotification.Message(expiringSoon, expired));
    }

    [Fact]
    public void Category_IsAccountTokenExpiry()
    {
        // Assert
        AccountTokenExpiryNotification.Category.ShouldBe("account-token-expiry");
    }

    [Fact]
    public void WarningWindow_IsFourteenDays()
    {
        // Assert
        AccountTokenExpiryNotification.WarningWindow.ShouldBe(TimeSpan.FromDays(14));
    }
}
