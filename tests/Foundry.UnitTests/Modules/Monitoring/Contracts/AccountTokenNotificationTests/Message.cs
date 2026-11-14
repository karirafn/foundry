using Foundry.Modules.Monitoring.Contracts;

using Shouldly;

using Xunit;

namespace Foundry.UnitTests.Modules.Monitoring.Contracts.AccountTokenNotificationTests;

public sealed class Message
{
    [Fact]
    public void WhenNoAccountIsInvalid_ReturnsEmpty()
    {
        // Act
        string message = AccountTokenNotification.Message(0);

        // Assert
        message.ShouldBeEmpty();
    }

    [Fact]
    public void WhenOneAccountIsInvalid_ReturnsSingularCopy()
    {
        // Act
        string message = AccountTokenNotification.Message(1);

        // Assert
        message.ShouldBe("1 account has an invalid token — enter a new one to resume monitoring.");
    }

    [Fact]
    public void WhenSeveralAccountsAreInvalid_ReturnsPluralCopy()
    {
        // Act
        string message = AccountTokenNotification.Message(3);

        // Assert
        message.ShouldBe("3 accounts have invalid tokens — enter new ones to resume monitoring.");
    }

    [Fact]
    public void WhenCountIsNegative_Throws()
    {
        // Act & Assert
        Should.Throw<ArgumentOutOfRangeException>(() => AccountTokenNotification.Message(-1));
    }

    [Fact]
    public void Category_IsAccountToken()
    {
        // Assert
        AccountTokenNotification.Category.ShouldBe("account-token");
    }
}
